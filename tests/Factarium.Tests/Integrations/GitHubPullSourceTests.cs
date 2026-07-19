using System.Text.Json;
using Factarium.Application.Sync;
using Factarium.Integrations.GitHub;
using Factarium.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Factarium.Tests.Integrations;

public class GitHubPullSourceTests
{
    [Fact]
    public async Task Syncs_repos_prs_reviews_commits_and_advances_cursors()
    {
        var (source, sink, cursor) = Build(out _);
        var context = Context(cursor, sink, initialPullCursor: null);

        var result = await source.PullAsync(context, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, sink.Facts.Count(f => f.EntityType == "repository"));
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "pull_request"));
        Assert.Equal(1, sink.Facts.Count(f => f.EntityType == "review"));
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "commit"));

        // Raw JSON is stored verbatim; source ids come from the payloads.
        Assert.Contains(sink.Facts, f => f is { EntityType: "pull_request", SourceId: "11" });
        Assert.Contains(sink.Facts, f => f is { EntityType: "commit", SourceId: "acme/repo1@bbb" });

        // Cursor advances to the newest values seen.
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-10T00:00:00Z"),
            DateTimeOffset.Parse(cursor.Get("pulls:acme/repo1")!));
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-06T00:00:00Z"),
            DateTimeOffset.Parse(cursor.Get("commits:acme/repo1")!));

        // Commit payloads are stored flat: only the fields Factarium uses, with no
        // nested objects, user blobs, verification or node ids.
        var commitFact = sink.Facts.Single(f => f is { EntityType: "commit", SourceId: "acme/repo1@aaa" });
        using var doc = JsonDocument.Parse(commitFact.Payload);
        var root = doc.RootElement;

        Assert.Equal("aaa", root.GetProperty("sha").GetString());
        Assert.Equal("acme/repo1", root.GetProperty("repo").GetString());
        Assert.Equal("t1", root.GetProperty("tree_sha").GetString());
        Assert.Equal("first\n\nCo-authored-by: Side Kick <side@e.com>", root.GetProperty("message").GetString());

        // The commit is attributed to its author: the resolved GitHub account
        // (id/login) plus the git author name/email.
        Assert.Equal(1, root.GetProperty("author_id").GetInt64());
        Assert.Equal("octo", root.GetProperty("author_login").GetString());
        Assert.Equal("Octo", root.GetProperty("author_name").GetString());
        Assert.Equal("o@e.com", root.GetProperty("author_email").GetString());
        Assert.Equal(0, root.GetProperty("comment_count").GetInt32());
        Assert.Equal(["p1"], root.GetProperty("parents").EnumerateArray().Select(p => p.GetString()));

        // Co-authored-by trailers are pulled out of the message as {name, email}.
        var coAuthors = root.GetProperty("co_authors");
        Assert.Equal(JsonValueKind.Array, coAuthors.ValueKind);
        var coAuthor = Assert.Single(coAuthors.EnumerateArray());
        Assert.Equal("Side Kick", coAuthor.GetProperty("name").GetString());
        Assert.Equal("side@e.com", coAuthor.GetProperty("email").GetString());

        // Nothing nested or bulky survives beyond the small co-author entries: no
        // embedded user blobs, verification or node ids.
        Assert.False(root.TryGetProperty("commit", out _));
        Assert.False(root.TryGetProperty("node_id", out _));
        Assert.False(root.TryGetProperty("committer", out _));
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
    }

    [Fact]
    public async Task Skips_pull_requests_at_or_before_the_cursor()
    {
        var (source, sink, cursor) = Build(out _);
        // Cursor already at the newest PR's updated_at: nothing new to pull.
        var context = Context(cursor, sink, initialPullCursor: "2026-07-10T00:00:00Z");

        await source.PullAsync(context, CancellationToken.None);

        Assert.DoesNotContain(sink.Facts, f => f.EntityType == "pull_request");
        Assert.DoesNotContain(sink.Facts, f => f.EntityType == "review");
    }

    private static (GitHubPullSource Source, RecordingRawRecordSink Sink, InMemoryCursorStore Cursor) Build(
        out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(Respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var client = new GitHubApiClient(http, TimeProvider.System, NullLogger<GitHubApiClient>.Instance);
        var source = new GitHubPullSource(client, NullLogger<GitHubPullSource>.Instance);
        return (source, new RecordingRawRecordSink(), new InMemoryCursorStore());
    }

    private static SyncContext Context(InMemoryCursorStore cursor, RecordingRawRecordSink sink, string? initialPullCursor)
    {
        if (initialPullCursor is not null)
        {
            cursor.Set("pulls:acme/repo1", initialPullCursor);
        }

        return new SyncContext
        {
            IntegrationId = Guid.NewGuid(),
            IntegrationName = "test",
            Credential = "token",
            Config = new GitHubSourceConfig(Org: null, Repos: ["acme/repo1"]),
            Cursor = cursor,
            Sink = sink,
        };
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri!.Query;

        return (path, page2: query.Contains("page=2")) switch
        {
            ("/repos/acme/repo1", _) => StubHttpMessageHandler.Json(
                """{"id":1,"full_name":"acme/repo1","updated_at":"2026-07-01T00:00:00Z"}"""),

            ("/repos/acme/repo1/pulls", true) => StubHttpMessageHandler.Json(
                """[{"id":12,"number":2,"state":"closed","updated_at":"2026-07-09T00:00:00Z"}]"""),

            ("/repos/acme/repo1/pulls", false) => StubHttpMessageHandler.Json(
                """[{"id":11,"number":1,"state":"open","updated_at":"2026-07-10T00:00:00Z"}]""",
                link: "<https://api.github.com/repos/acme/repo1/pulls?state=all&sort=updated&direction=desc&per_page=100&page=2>; rel=\"next\""),

            ("/repos/acme/repo1/pulls/1/reviews", _) => StubHttpMessageHandler.Json(
                """[{"id":111,"state":"APPROVED","submitted_at":"2026-07-10T01:00:00Z"}]"""),

            ("/repos/acme/repo1/pulls/2/reviews", _) => StubHttpMessageHandler.Json("[]"),

            ("/repos/acme/repo1/commits", _) => StubHttpMessageHandler.Json(
                """
                [
                  {
                    "sha": "aaa",
                    "node_id": "N_aaa",
                    "url": "https://api.github.com/repos/acme/repo1/commits/aaa",
                    "html_url": "https://github.com/acme/repo1/commit/aaa",
                    "comments_url": "https://api.github.com/repos/acme/repo1/commits/aaa/comments",
                    "author": { "login": "octo", "id": 1, "node_id": "U_1", "type": "User", "html_url": "https://github.com/octo", "avatar_url": "https://a/1" },
                    "committer": { "login": "octo", "id": 1, "node_id": "U_1", "type": "User", "html_url": "https://github.com/octo", "avatar_url": "https://a/1" },
                    "parents": [{ "sha": "p1" }],
                    "commit": {
                      "url": "https://api.github.com/repos/acme/repo1/git/commits/aaa",
                      "tree": { "sha": "t1", "url": "tu" },
                      "message": "first\n\nCo-authored-by: Side Kick <side@e.com>",
                      "author": { "date": "2026-07-05T00:00:00Z", "name": "Octo", "email": "o@e.com" },
                      "committer": { "date": "2026-07-05T00:00:00Z", "name": "Octo", "email": "o@e.com" },
                      "verification": { "verified": false, "reason": "unsigned" },
                      "comment_count": 0
                    }
                  },
                  {
                    "sha": "bbb",
                    "node_id": "N_bbb",
                    "parents": [{ "sha": "aaa" }],
                    "committer": { "login": "octo", "id": 1, "node_id": "U_1", "type": "User", "html_url": "https://github.com/octo", "avatar_url": "https://a/1" },
                    "commit": {
                      "message": "second",
                      "committer": { "date": "2026-07-06T00:00:00Z", "name": "Octo", "email": "o@e.com" },
                      "comment_count": 2
                    }
                  }
                ]
                """),

            _ => StubHttpMessageHandler.NotFound(),
        };
    }
}
