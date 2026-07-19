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

        var results = await PullSourceRunner.RunAllAsync(source, context);

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(1, sink.Facts.Count(f => f.EntityType == "repository"));
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "pull_request"));
        Assert.Equal(1, sink.Facts.Count(f => f.EntityType == "review"));
        Assert.Equal(2, sink.Facts.Count(f => f.EntityType == "commit"));

        // Source ids come from the (flattened) payloads.
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

        // Pull requests are flattened from the detail endpoint: author, branches,
        // state and comment/diff stats as scalars, with no nested user/repo blobs.
        var prFact = sink.Facts.Single(f => f is { EntityType: "pull_request", SourceId: "11" });
        using var prDoc = JsonDocument.Parse(prFact.Payload);
        var pr = prDoc.RootElement;

        Assert.Equal(1, pr.GetProperty("number").GetInt32());
        Assert.Equal("acme/repo1", pr.GetProperty("repository_full_name").GetString());
        Assert.Equal("open", pr.GetProperty("state").GetString());
        Assert.False(pr.GetProperty("draft").GetBoolean());
        Assert.Equal(1, pr.GetProperty("author_id").GetInt64());
        Assert.Equal("octo", pr.GetProperty("author_login").GetString());
        Assert.Equal("main", pr.GetProperty("base_ref").GetString());
        Assert.Equal("feature-1", pr.GetProperty("head_ref").GetString());
        Assert.Equal("acme/repo1", pr.GetProperty("head_repo").GetString());
        Assert.Equal(3, pr.GetProperty("comment_count").GetInt32());
        Assert.Equal(2, pr.GetProperty("review_comment_count").GetInt32());
        Assert.Equal(2, pr.GetProperty("changed_files").GetInt32());

        Assert.False(pr.TryGetProperty("user", out _));
        Assert.False(pr.TryGetProperty("base", out _));
        Assert.False(pr.TryGetProperty("head", out _));
        foreach (var property in pr.EnumerateObject())
        {
            Assert.NotEqual(JsonValueKind.Object, property.Value.ValueKind);
        }

        // Reviews are flattened too: reviewer/state/provenance as scalars, no user
        // blob or body.
        var reviewFact = sink.Facts.Single(f => f.EntityType == "review");
        using var reviewDoc = JsonDocument.Parse(reviewFact.Payload);
        var review = reviewDoc.RootElement;

        Assert.Equal(111, review.GetProperty("id").GetInt64());
        Assert.Equal("acme/repo1", review.GetProperty("repository_full_name").GetString());
        Assert.Equal(1, review.GetProperty("pull_request_number").GetInt32());
        Assert.Equal("octo", review.GetProperty("reviewer_login").GetString());
        Assert.Equal(1, review.GetProperty("reviewer_id").GetInt64());
        Assert.Equal("APPROVED", review.GetProperty("state").GetString());
        Assert.Equal("dc8", review.GetProperty("commit_id").GetString());
        Assert.False(review.TryGetProperty("user", out _));
        Assert.False(review.TryGetProperty("body", out _));

        // The repository is flattened to identity, owner and header fields; the
        // nested owner/license/permissions blobs are dropped.
        var repoFact = sink.Facts.Single(f => f.EntityType == "repository");
        using var repoDoc = JsonDocument.Parse(repoFact.Payload);
        var repository = repoDoc.RootElement;

        Assert.Equal("acme/repo1", repository.GetProperty("full_name").GetString());
        Assert.Equal("repo1", repository.GetProperty("name").GetString());
        Assert.Equal("acme", repository.GetProperty("owner_login").GetString());
        Assert.Equal(42, repository.GetProperty("owner_id").GetInt64());
        Assert.Equal("C#", repository.GetProperty("language").GetString());
        Assert.Equal(7, repository.GetProperty("stargazers_count").GetInt32());
        Assert.False(repository.TryGetProperty("owner", out _));
        Assert.False(repository.TryGetProperty("permissions", out _));
        Assert.False(repository.TryGetProperty("license", out _));
        foreach (var property in repository.EnumerateObject())
        {
            Assert.NotEqual(JsonValueKind.Object, property.Value.ValueKind);
        }
    }

    [Fact]
    public async Task Skips_pull_requests_at_or_before_the_cursor()
    {
        var (source, sink, cursor) = Build(out _);
        // Cursor already at the newest PR's updated_at: nothing new to pull.
        var context = Context(cursor, sink, initialPullCursor: "2026-07-10T00:00:00Z");

        await PullSourceRunner.RunAllAsync(source, context);

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
            Reader = sink,
        };
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri!.Query;

        return (path, page2: query.Contains("page=2")) switch
        {
            ("/repos/acme/repo1", _) => StubHttpMessageHandler.Json(
                """
                {
                  "id": 1, "node_id": "R_1", "full_name": "acme/repo1", "name": "repo1",
                  "owner": { "login": "acme", "id": 42, "type": "Organization", "node_id": "O_42" },
                  "description": "A test repo", "html_url": "https://github.com/acme/repo1",
                  "default_branch": "main", "language": "C#", "visibility": "public",
                  "stargazers_count": 7, "forks_count": 2, "open_issues_count": 3,
                  "pushed_at": "2026-07-01T00:00:00Z", "created_at": "2026-06-01T00:00:00Z",
                  "updated_at": "2026-07-01T00:00:00Z",
                  "permissions": { "admin": true }, "license": { "key": "mit" }
                }
                """),

            ("/repos/acme/repo1/pulls", true) => StubHttpMessageHandler.Json(
                """[{"id":12,"number":2,"state":"closed","updated_at":"2026-07-09T00:00:00Z"}]"""),

            ("/repos/acme/repo1/pulls", false) => StubHttpMessageHandler.Json(
                """[{"id":11,"number":1,"state":"open","updated_at":"2026-07-10T00:00:00Z"}]""",
                link: "<https://api.github.com/repos/acme/repo1/pulls?state=all&sort=updated&direction=desc&per_page=100&page=2>; rel=\"next\""),

            ("/repos/acme/repo1/pulls/1", _) => StubHttpMessageHandler.Json(
                """
                {
                  "id": 11, "number": 1, "state": "open", "title": "Add feature", "draft": false,
                  "node_id": "PR_11",
                  "user": { "login": "octo", "id": 1, "node_id": "U_1", "type": "User" },
                  "base": { "ref": "main", "repo": { "full_name": "acme/repo1", "id": 1 } },
                  "head": { "ref": "feature-1", "repo": { "full_name": "acme/repo1", "id": 1 } },
                  "created_at": "2026-07-08T00:00:00Z",
                  "updated_at": "2026-07-10T00:00:00Z",
                  "closed_at": null, "merged_at": null, "merge_commit_sha": null,
                  "comments": 3, "review_comments": 2,
                  "additions": 10, "deletions": 4, "changed_files": 2, "commits": 5
                }
                """),

            ("/repos/acme/repo1/pulls/2", _) => StubHttpMessageHandler.Json(
                """
                {
                  "id": 12, "number": 2, "state": "closed", "title": "Fix bug", "draft": false,
                  "user": { "login": "octo", "id": 1 },
                  "base": { "ref": "main" },
                  "head": { "ref": "fix-2" },
                  "created_at": "2026-07-07T00:00:00Z",
                  "updated_at": "2026-07-09T00:00:00Z",
                  "closed_at": "2026-07-09T00:00:00Z",
                  "merged_at": "2026-07-09T00:00:00Z",
                  "merge_commit_sha": "mmm",
                  "comments": 0, "review_comments": 0,
                  "additions": 1, "deletions": 1, "changed_files": 1, "commits": 1
                }
                """),

            ("/repos/acme/repo1/pulls/1/reviews", _) => StubHttpMessageHandler.Json(
                """[{"id":111,"state":"APPROVED","submitted_at":"2026-07-10T01:00:00Z","commit_id":"dc8","user":{"login":"octo","id":1,"node_id":"U_1"},"body":"lgtm"}]"""),

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
