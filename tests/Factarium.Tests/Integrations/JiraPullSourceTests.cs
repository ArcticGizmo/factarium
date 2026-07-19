using Factarium.Application.Sync;
using Factarium.Integrations.Jira;
using Factarium.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factarium.Tests.Integrations;

public class JiraPullSourceTests
{
    [Fact]
    public async Task Syncs_issues_changelog_sprints_and_devlinks_then_advances_cursor()
    {
        string? searchBody = null;
        var handler = new StubHttpMessageHandler(request => Respond(request, ref searchBody));
        var http = new HttpClient(handler);
        var client = new JiraApiClient(http, TimeProvider.System, NullLogger<JiraApiClient>.Instance);
        var source = new JiraPullSource(client, NullLogger<JiraPullSource>.Instance);

        var sink = new RecordingRawRecordSink();
        var cursor = new InMemoryCursorStore();
        var context = new SyncContext
        {
            IntegrationId = Guid.NewGuid(),
            IntegrationName = "jira",
            Credential = "api-token",
            Config = new JiraSourceConfig(
                BaseUrl: "https://acme.atlassian.net",
                Email: "dev@example.com",
                ProjectKey: "QAI",
                SyncSince: DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                ScopedToken: false),
            Cursor = cursor,
            Sink = sink,
        };

        var result = await source.PullAsync(context, CancellationToken.None);

        Assert.True(result.Succeeded);

        // One fact per bronze entity type.
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "issue"));
        Assert.Contains(sink.Facts, f => f is { EntityType: "issue", SourceId: "1001" });
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "issue_changelog"));
        Assert.Contains(sink.Facts, f => f is { EntityType: "sprint", SourceId: "5" });
        Assert.Contains(sink.Facts, f => f is { EntityType: "issue_devlinks", SourceId: "1001" });

        // Sprints are derived from the issue Sprint field, so the Agile API is never called
        // (its scope isn't grantable to scoped tokens).
        Assert.DoesNotContain(handler.Requests, r => r.Contains("/rest/agile/"));

        // Jira's local-offset timestamps (here +10:00) are normalized to zero-offset UTC.
        var issue1001 = sink.Facts.Single(f => f is { EntityType: "issue", SourceId: "1001" });
        Assert.Equal(TimeSpan.Zero, issue1001.SourceUpdatedAt!.Value.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T12:00:00Z"), issue1001.SourceUpdatedAt);

        // JQL is scoped to the project and floored at the sync-since date. (JSON HTML-escapes
        // the quotes and ">=", so assert on the escaping-agnostic pieces.)
        Assert.NotNull(searchBody);
        Assert.Contains("project = ", searchBody);
        Assert.Contains("QAI", searchBody);
        Assert.Contains("updated", searchBody);
        Assert.Contains("2026-07-01", searchBody);

        // Cursor advances to the newest issue-updated time seen.
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-11T09:00:00Z"),
            DateTimeOffset.Parse(cursor.Get("issues:updated")!));
    }

    [Fact]
    public async Task Fails_without_required_settings()
    {
        var handler = new StubHttpMessageHandler(request => StubHttpMessageHandler.NotFound());
        var http = new HttpClient(handler);
        var client = new JiraApiClient(http, TimeProvider.System, NullLogger<JiraApiClient>.Instance);
        var source = new JiraPullSource(client, NullLogger<JiraPullSource>.Instance);

        var context = new SyncContext
        {
            IntegrationId = Guid.NewGuid(),
            IntegrationName = "jira",
            Credential = "api-token",
            Config = new JiraSourceConfig(
                BaseUrl: null, Email: null, ProjectKey: null, SyncSince: null, ScopedToken: false),
            Cursor = new InMemoryCursorStore(),
            Sink = new RecordingRawRecordSink(),
        };

        var result = await source.PullAsync(context, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Scoped_token_routes_through_the_api_gateway_and_skips_devlinks()
    {
        var handler = new StubHttpMessageHandler(ScopedRespond);
        var http = new HttpClient(handler);
        var client = new JiraApiClient(http, TimeProvider.System, NullLogger<JiraApiClient>.Instance);
        var source = new JiraPullSource(client, NullLogger<JiraPullSource>.Instance);

        var sink = new RecordingRawRecordSink();
        var cursor = new InMemoryCursorStore();
        var context = new SyncContext
        {
            IntegrationId = Guid.NewGuid(),
            IntegrationName = "jira",
            Credential = "scoped-token",
            Config = new JiraSourceConfig(
                BaseUrl: "https://acme.atlassian.net",
                Email: "dev@example.com",
                ProjectKey: "QAI",
                SyncSince: null,
                ScopedToken: true),
            Cursor = cursor,
            Sink = sink,
        };

        var result = await source.PullAsync(context, CancellationToken.None);

        Assert.True(result.Succeeded);
        // Cloud id resolved from the site and cached; issue/search calls hit the gateway.
        Assert.Contains(handler.Requests, r => r.EndsWith("/_edge/tenant_info"));
        Assert.Contains(handler.Requests, r => r.StartsWith("https://api.atlassian.com/ex/jira/cloud-123/rest/api/3/search/jql"));
        Assert.Equal("cloud-123", cursor.Get("meta:cloudId"));
        // Dev-status is never called in scoped mode.
        Assert.DoesNotContain(handler.Requests, r => r.Contains("/rest/dev-status/"));
        Assert.DoesNotContain(sink.Facts, f => f.EntityType == "issue_devlinks");
        Assert.Single(sink.Facts, f => f.EntityType == "issue");
    }

    private static HttpResponseMessage ScopedRespond(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/_edge/tenant_info"))
        {
            return StubHttpMessageHandler.Json("""{ "cloudId": "cloud-123" }""");
        }

        // Under the gateway the product paths are prefixed with /ex/jira/{cloudId}.
        var path = request.RequestUri.AbsolutePath.Replace("/ex/jira/cloud-123", "");
        return path switch
        {
            "/rest/api/3/field" => StubHttpMessageHandler.Json(
                """[ { "id": "customfield_10016", "name": "Story Points" } ]"""),
            "/rest/api/3/search/jql" => StubHttpMessageHandler.Json(
                """
                { "issues": [ { "id": "2001", "key": "QAI-9",
                  "fields": { "updated": "2026-07-12T09:00:00.000+0000" } } ] }
                """),
            "/rest/api/3/issue/2001/changelog" => StubHttpMessageHandler.Json("""{ "isLast": true, "values": [] }"""),
            _ => StubHttpMessageHandler.NotFound(),
        };
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, ref string? searchBody)
    {
        var path = request.RequestUri!.AbsolutePath;

        switch (path)
        {
            case "/rest/api/3/field":
                return StubHttpMessageHandler.Json(
                    """
                    [
                      { "id": "summary", "name": "Summary" },
                      { "id": "customfield_10016", "name": "Story point estimate" },
                      { "id": "customfield_10020", "name": "Sprint" }
                    ]
                    """);

            case "/rest/api/3/search/jql":
                searchBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return StubHttpMessageHandler.Json(
                    """
                    {
                      "issues": [
                        { "id": "1001", "key": "QAI-1",
                          "fields": { "updated": "2026-07-10T22:00:00.000+1000",
                            "created": "2026-07-01T09:00:00.000+0000",
                            "status": { "name": "Done", "statusCategory": { "key": "done" } },
                            "assignee": { "accountId": "acc-1", "displayName": "Ada" },
                            "issuetype": { "name": "Story" }, "project": { "key": "QAI" },
                            "customfield_10016": 5,
                            "customfield_10020": [
                              { "id": 5, "name": "Sprint 5", "state": "closed", "boardId": 10,
                                "startDate": "2026-07-02T09:00:00.000Z",
                                "endDate": "2026-07-16T09:00:00.000Z",
                                "completeDate": "2026-07-15T18:00:00.000Z" } ],
                            "resolutiondate": "2026-07-09T15:00:00.000+0000" } },
                        { "id": "1002", "key": "QAI-2",
                          "fields": { "updated": "2026-07-11T09:00:00.000+0000",
                            "created": "2026-07-05T09:00:00.000+0000",
                            "status": { "name": "In Progress", "statusCategory": { "key": "indeterminate" } },
                            "assignee": null,
                            "issuetype": { "name": "Bug" }, "project": { "key": "QAI" },
                            "resolutiondate": null } }
                      ]
                    }
                    """);

            case "/rest/api/3/issue/1001/changelog":
            case "/rest/api/3/issue/1002/changelog":
                return StubHttpMessageHandler.Json(
                    """
                    {
                      "isLast": true,
                      "values": [
                        { "created": "2026-07-08T10:00:00.000+0000",
                          "author": { "accountId": "acc-1" },
                          "items": [ { "field": "status", "fromString": "To Do", "toString": "In Progress" } ] }
                      ]
                    }
                    """);

            case "/rest/dev-status/1.0/issue/detail":
                // Return a PR only for issue 1001's pull-request lookup; empty otherwise.
                var query = request.RequestUri!.Query;
                var isPr = query.Contains("dataType=pullrequest");
                var forIssue1001 = query.Contains("issueId=1001");
                if (isPr && forIssue1001)
                {
                    return StubHttpMessageHandler.Json(
                        """
                        { "detail": [ { "pullRequests": [
                            { "id": "#42", "status": "MERGED", "url": "https://github.com/acme/api/pull/42",
                              "lastUpdate": "2026-07-09T10:00:00.000Z" } ], "branches": [] } ] }
                        """);
                }

                return StubHttpMessageHandler.Json("""{ "detail": [] }""");

            default:
                return StubHttpMessageHandler.NotFound();
        }
    }
}
