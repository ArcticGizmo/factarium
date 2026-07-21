using System.Text.Json;
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
            Reader = sink,
        };

        var results = await PullSourceRunner.RunAllAsync(source, context);

        Assert.All(results, r => Assert.True(r.Succeeded));

        // One fact per bronze entity type.
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "issue"));
        Assert.Contains(sink.Facts, f => f is { EntityType: "issue", SourceId: "1001" });
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "issue_changelog"));

        // The changelog is stored as a compact projection: only tracked-field changes survive
        // (the status change is kept; worklog noise is dropped), the entry keeps just the author
        // account (no email/avatar blob), and empty-of-signal entries are omitted entirely.
        var changelog1001 = sink.Facts.Single(f => f is { EntityType: "issue_changelog", SourceId: "1001" });
        using var changelogDoc = JsonDocument.Parse(changelog1001.Payload);
        var changelogRoot = changelogDoc.RootElement;
        Assert.False(changelogRoot.TryGetProperty("histories", out _));
        var changelogEntries = changelogRoot.GetProperty("entries");
        Assert.Equal(1, changelogEntries.GetArrayLength());
        var entry = changelogEntries[0];
        Assert.Equal("acc-1", entry.GetProperty("author_id").GetString());
        Assert.Equal("Ada L", entry.GetProperty("author_name").GetString());
        Assert.False(entry.TryGetProperty("emailAddress", out _));
        var changes = entry.GetProperty("changes");
        Assert.Equal(1, changes.GetArrayLength());
        Assert.Equal("status", changes[0].GetProperty("field").GetString());
        Assert.Equal("In Progress", changes[0].GetProperty("to_str").GetString());

        Assert.Contains(sink.Facts, f => f is { EntityType: "sprint", SourceId: "5" });
        // Future sprints (state "future", here QAI-2's Sprint 6) are not replicated.
        Assert.DoesNotContain(sink.Facts, f => f is { EntityType: "sprint", SourceId: "6" });
        Assert.Contains(sink.Facts, f => f is { EntityType: "issue_devlinks", SourceId: "1001" });

        // Sprints are derived from the issue Sprint field, so the Agile API is never called
        // (its scope isn't grantable to scoped tokens).
        Assert.DoesNotContain(handler.Requests, r => r.Contains("/rest/agile/"));

        // Jira's local-offset timestamps (here +10:00) are normalized to zero-offset UTC.
        var issue1001 = sink.Facts.Single(f => f is { EntityType: "issue", SourceId: "1001" });
        Assert.Equal(TimeSpan.Zero, issue1001.SourceUpdatedAt!.Value.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-07-10T12:00:00Z"), issue1001.SourceUpdatedAt);

        // The issue is stored flattened (GitHub-style projection), not the raw Jira payload:
        // flat root-level keys, no nested "fields" envelope, and no description (ADF).
        using var issueDoc = JsonDocument.Parse(issue1001.Payload);
        var flat = issueDoc.RootElement;
        Assert.False(flat.TryGetProperty("fields", out _));
        Assert.False(flat.TryGetProperty("description", out _));
        Assert.Equal("Export button fails on Safari", flat.GetProperty("title").GetString());
        Assert.Equal("10001", flat.GetProperty("issue_type_id").GetString());
        Assert.Equal("Story", flat.GetProperty("issue_type").GetString());
        Assert.Equal("Done", flat.GetProperty("status_category").GetString());
        Assert.Equal("done", flat.GetProperty("status_category_key").GetString());
        Assert.Equal(5, flat.GetProperty("story_points").GetDouble());
        Assert.Equal(3, flat.GetProperty("comment_count").GetInt32());
        Assert.Equal("acc-1", flat.GetProperty("assignee_id").GetString());
        Assert.Equal("acc-2", flat.GetProperty("reporter_id").GetString());
        Assert.Equal("acc-3", flat.GetProperty("primary_developer_id").GetString());
        Assert.Equal(5, flat.GetProperty("sprints")[0].GetProperty("id").GetInt64());

        // JQL is scoped to the project and floored at the sync-since date. (JSON HTML-escapes
        // the quotes and ">=", so assert on the escaping-agnostic pieces.)
        Assert.NotNull(searchBody);
        Assert.Contains("project = ", searchBody);
        Assert.Contains("QAI", searchBody);
        Assert.Contains("updated", searchBody);
        Assert.Contains("2026-07-01", searchBody);

        // The requested field list is minimized: reporter/comment and the discovered
        // primary-developer custom field are requested; the unused "parent" is not.
        Assert.Contains("reporter", searchBody);
        Assert.Contains("comment", searchBody);
        Assert.Contains("customfield_10050", searchBody);
        Assert.DoesNotContain("parent", searchBody);

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
            Reader = new RecordingRawRecordSink(),
        };

        var result = await source.PullEntityAsync("issue", context, CancellationToken.None);

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
            Reader = sink,
        };

        var results = await PullSourceRunner.RunAllAsync(source, context);

        Assert.All(results, r => Assert.True(r.Succeeded));
        // Cloud id resolved from the site and cached; issue/search calls hit the gateway.
        Assert.Contains(handler.Requests, r => r.EndsWith("/_edge/tenant_info"));
        Assert.Contains(handler.Requests, r => r.StartsWith("https://api.atlassian.com/ex/jira/cloud-123/rest/api/3/search/jql"));
        Assert.Equal("cloud-123", cursor.Get("meta:cloudId"));
        // Dev-status is never called in scoped mode.
        Assert.DoesNotContain(handler.Requests, r => r.Contains("/rest/dev-status/"));
        Assert.DoesNotContain(sink.Facts, f => f.EntityType == "issue_devlinks");
        Assert.Single(sink.Facts, f => f.EntityType == "issue");
    }

    [Fact]
    public async Task Failing_entity_does_not_discard_a_committed_entity()
    {
        var handler = new StubHttpMessageHandler(IsolationRespond);
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
                SyncSince: null,
                ScopedToken: false),
            Cursor = cursor,
            Sink = sink,
            Reader = sink,
        };

        // The issue entity commits its records and advances its own cursor.
        var issueResult = await source.PullEntityAsync("issue", context, CancellationToken.None);
        Assert.True(issueResult.Succeeded);
        Assert.Contains(sink.Facts, f => f.EntityType == "issue");
        Assert.NotNull(cursor.Get("issues:updated"));

        // The changelog entity then fails — but the issue records and cursor are untouched.
        await Assert.ThrowsAnyAsync<Exception>(
            () => source.PullEntityAsync("issue_changelog", context, CancellationToken.None));
        Assert.Contains(sink.Facts, f => f.EntityType == "issue");
        Assert.NotNull(cursor.Get("issues:updated"));
        Assert.DoesNotContain(sink.Facts, f => f.EntityType == "issue_changelog");
    }

    private static HttpResponseMessage IsolationRespond(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath switch
        {
            "/rest/api/3/field" => StubHttpMessageHandler.Json(
                """[ { "id": "customfield_10016", "name": "Story Points" } ]"""),
            "/rest/api/3/search/jql" => StubHttpMessageHandler.Json(
                """
                { "issues": [ { "id": "3001", "key": "QAI-3",
                  "fields": { "updated": "2026-07-12T09:00:00.000+0000" } } ] }
                """),
            // Changelog fails: the entity throws, but must not roll back the issue entity.
            "/rest/api/3/issue/3001/changelog" => StubHttpMessageHandler.NotFound(),
            _ => StubHttpMessageHandler.NotFound(),
        };

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
                      { "id": "customfield_10020", "name": "Sprint" },
                      { "id": "customfield_10050", "name": "Primary Developer" }
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
                            "summary": "Export button fails on Safari",
                            "status": { "name": "Done", "statusCategory": { "key": "done", "name": "Done" } },
                            "assignee": { "accountId": "acc-1", "displayName": "Ada" },
                            "reporter": { "accountId": "acc-2", "displayName": "Bob" },
                            "issuetype": { "id": "10001", "name": "Story" }, "project": { "key": "QAI" },
                            "comment": { "total": 3 },
                            "customfield_10016": 5,
                            "customfield_10050": { "accountId": "acc-3", "displayName": "Cid" },
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
                            "customfield_10020": [
                              { "id": 6, "name": "Sprint 6", "state": "future", "boardId": 10,
                                "startDate": "2026-08-01T09:00:00.000Z",
                                "endDate": "2026-08-15T09:00:00.000Z" } ],
                            "resolutiondate": null } }
                      ]
                    }
                    """);

            case "/rest/api/3/issue/1001/changelog":
            case "/rest/api/3/issue/1002/changelog":
                // The status change is tracked; the worklog entry (and the worklog item mixed
                // into the status entry) is noise the projection must drop.
                return StubHttpMessageHandler.Json(
                    """
                    {
                      "isLast": true,
                      "values": [
                        { "created": "2026-07-08T10:00:00.000+0000",
                          "author": { "accountId": "acc-1", "displayName": "Ada L", "emailAddress": "ada@example.com" },
                          "items": [
                            { "field": "status", "fieldId": "status", "fromString": "To Do", "toString": "In Progress" },
                            { "field": "timespent", "fieldId": "timespent", "from": null, "to": "3600" } ] },
                        { "created": "2026-07-08T11:00:00.000+0000",
                          "author": { "accountId": "acc-1" },
                          "items": [ { "field": "WorklogId", "from": null, "to": "90210" } ] }
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
