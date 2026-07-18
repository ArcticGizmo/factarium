using Factarium.Application.Sync;
using Factarium.Integrations.Jira;
using Factarium.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factarium.Tests.Integrations;

public class JiraPullSourceTests
{
    [Fact]
    public async Task Syncs_issues_and_advances_cursor()
    {
        var handler = new StubHttpMessageHandler(Respond);
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
                ProjectKeys: ["QAI"],
                Jql: null),
            Cursor = cursor,
            Sink = sink,
        };

        var result = await source.PullAsync(context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "issue"));
        Assert.Contains(sink.Facts, f => f is { EntityType: "issue", SourceId: "1001" });
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-11T09:00:00Z"),
            DateTimeOffset.Parse(cursor.Get("issues:updated")!));
    }

    [Fact]
    public async Task Fails_without_required_settings()
    {
        var handler = new StubHttpMessageHandler(Respond);
        var http = new HttpClient(handler);
        var client = new JiraApiClient(http, TimeProvider.System, NullLogger<JiraApiClient>.Instance);
        var source = new JiraPullSource(client, NullLogger<JiraPullSource>.Instance);

        var context = new SyncContext
        {
            IntegrationId = Guid.NewGuid(),
            IntegrationName = "jira",
            Credential = "api-token",
            Config = new JiraSourceConfig(BaseUrl: null, Email: null, ProjectKeys: [], Jql: null),
            Cursor = new InMemoryCursorStore(),
            Sink = new RecordingRawRecordSink(),
        };

        var result = await source.PullAsync(context, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath == "/rest/api/3/search")
        {
            return StubHttpMessageHandler.Json(
                """
                {
                  "total": 2,
                  "issues": [
                    { "id": "1001", "key": "QAI-1",
                      "fields": { "updated": "2026-07-10T12:00:00.000+0000",
                        "created": "2026-07-01T09:00:00.000+0000",
                        "status": { "name": "Done", "statusCategory": { "key": "done" } },
                        "assignee": { "accountId": "acc-1", "displayName": "Ada" },
                        "issuetype": { "name": "Story" }, "project": { "key": "QAI" },
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
        }

        return StubHttpMessageHandler.NotFound();
    }
}
