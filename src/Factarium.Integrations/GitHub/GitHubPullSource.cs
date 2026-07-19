using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.GitHub;

/// <summary>
/// Pull source for GitHub. Resolves repositories (from an org and/or an explicit
/// list), then incrementally syncs pull requests (with reviews) and commits into
/// the bronze tier, storing the raw GitHub JSON and advancing per-repo cursors.
/// </summary>
public sealed class GitHubPullSource(GitHubApiClient client, ILogger<GitHubPullSource> logger) : IPullSource
{
    private const string Source = "github";

    // "Co-authored-by: Name <email>" trailers — GitHub's convention for crediting
    // additional authors on a commit. Matched case-insensitively, one per line.
    private static readonly Regex CoAuthorTrailer = new(
        @"^\s*Co-authored-by:\s*(?<name>[^<>\n]*?)\s*<(?<email>[^>\n]+)>\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public string Type => Source;

    public async Task<SyncResult> PullAsync(SyncContext context, CancellationToken cancellationToken)
    {
        if (context.Config is not GitHubSourceConfig config)
        {
            return SyncResult.Failed("GitHub integration is misconfigured (expected GitHub configuration).");
        }

        var token = context.Credential;
        var (repositories, written) = await ResolveRepositoriesAsync(context, config, token, cancellationToken);

        if (repositories.Count == 0)
        {
            return SyncResult.Failed("No repositories resolved. Set an organization and/or specific repos.");
        }

        foreach (var fullName in repositories)
        {
            written += await SyncPullRequestsAsync(context, fullName, token, cancellationToken);
            written += await SyncCommitsAsync(context, fullName, token, cancellationToken);
        }

        return SyncResult.Ok(written);
    }

    private async Task<(List<string> Repositories, int Written)> ResolveRepositoriesAsync(
        SyncContext context, GitHubSourceConfig config, string? token, CancellationToken cancellationToken)
    {
        var repositories = new List<string>();
        var facts = new List<RawFact>();

        // Explicit repos: "owner/name" slugs.
        foreach (var slug in config.Repos.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()))
        {
            var repo = await client.GetObjectAsync($"repos/{slug}", token, cancellationToken);
            if (repo is { } element)
            {
                facts.Add(RepositoryFact(element));
                repositories.Add(slug);
            }
            else
            {
                logger.LogWarning("GitHub repository {Slug} not found or inaccessible", slug);
            }
        }

        // Org enumeration.
        if (!string.IsNullOrWhiteSpace(config.Org))
        {
            var org = config.Org;
            await foreach (var repo in client.GetPagedAsync($"orgs/{org}/repos?per_page=100", token, cancellationToken))
            {
                var fullName = repo.GetProperty("full_name").GetString();
                if (fullName is null || repositories.Contains(fullName))
                {
                    continue;
                }

                facts.Add(RepositoryFact(repo));
                repositories.Add(fullName);
            }
        }

        var written = await context.Sink.WriteAsync(context.IntegrationId, facts, cancellationToken);
        return (repositories, written);
    }

    private async Task<int> SyncPullRequestsAsync(
        SyncContext context, string fullName, string? token, CancellationToken cancellationToken)
    {
        var cursorKey = $"pulls:{fullName}";
        var lastCursor = ParseTimestamp(context.Cursor.Get(cursorKey));
        var facts = new List<RawFact>();
        DateTimeOffset? maxUpdated = lastCursor;

        var url = $"repos/{fullName}/pulls?state=all&sort=updated&direction=desc&per_page=100";
        await foreach (var pr in client.GetPagedAsync(url, token, cancellationToken))
        {
            var updatedAt = GetTimestamp(pr, "updated_at");

            // Sorted newest-first: once we reach records at/older than the cursor, stop.
            if (lastCursor is not null && updatedAt is not null && updatedAt <= lastCursor)
            {
                break;
            }

            var number = pr.GetProperty("number").GetInt32();

            // The list endpoint omits comment counts and diff stats, so fetch the
            // PR detail (a superset) and flatten that; fall back to the list object
            // if it 404s (e.g. deleted between pages).
            var detail = await client.GetObjectAsync($"repos/{fullName}/pulls/{number}", token, cancellationToken);
            var payload = detail ?? pr;
            facts.Add(new RawFact(Source, "pull_request", Id(payload),
                FlattenPullRequest(payload, fullName), updatedAt));
            if (updatedAt is not null && (maxUpdated is null || updatedAt > maxUpdated))
            {
                maxUpdated = updatedAt;
            }

            await foreach (var review in client.GetPagedAsync(
                               $"repos/{fullName}/pulls/{number}/reviews?per_page=100", token, cancellationToken))
            {
                facts.Add(new RawFact(Source, "review", Id(review),
                    FlattenReview(review, fullName, number),
                    GetTimestamp(review, "submitted_at")));
            }
        }

        var written = await context.Sink.WriteAsync(context.IntegrationId, facts, cancellationToken);
        if (maxUpdated is not null && maxUpdated != lastCursor)
        {
            context.Cursor.Set(cursorKey, maxUpdated.Value.ToUniversalTime().ToString("o"));
        }

        return written;
    }

    private async Task<int> SyncCommitsAsync(
        SyncContext context, string fullName, string? token, CancellationToken cancellationToken)
    {
        var cursorKey = $"commits:{fullName}";
        var lastCursor = ParseTimestamp(context.Cursor.Get(cursorKey));
        var facts = new List<RawFact>();
        DateTimeOffset? maxCommitted = lastCursor;

        var url = $"repos/{fullName}/commits?per_page=100";
        if (lastCursor is not null)
        {
            url += $"&since={lastCursor.Value.ToUniversalTime():o}";
        }

        await foreach (var commit in client.GetPagedAsync(url, token, cancellationToken))
        {
            var sha = commit.GetProperty("sha").GetString()!;
            var committedAt = CommitDate(commit);

            facts.Add(new RawFact(Source, "commit", $"{fullName}@{sha}",
                FlattenCommit(commit, fullName), committedAt));
            if (committedAt is not null && (maxCommitted is null || committedAt > maxCommitted))
            {
                maxCommitted = committedAt;
            }
        }

        var written = await context.Sink.WriteAsync(context.IntegrationId, facts, cancellationToken);
        if (maxCommitted is not null && maxCommitted != lastCursor)
        {
            context.Cursor.Set(cursorKey, maxCommitted.Value.ToUniversalTime().ToString("o"));
        }

        return written;
    }

    /// <summary>
    /// Flattens a GitHub pull request into the fields Factarium uses: identity
    /// (id, number, repo), the author, source/target branches, draft/state/merge
    /// status, lifecycle timestamps, the merge commit, and comment/diff stats.
    /// Comment counts and diff stats only exist on the PR detail endpoint, so the
    /// caller passes the detail object here. Nested user/repo blobs are discarded.
    /// </summary>
    private static string FlattenPullRequest(JsonElement pr, string fullName)
    {
        var user = Object(pr, "user");
        var baseRef = Object(pr, "base");
        var head = Object(pr, "head");

        var node = new JsonObject
        {
            ["id"] = Num(pr, "id"),
            ["number"] = Num(pr, "number"),
            ["repository_full_name"] = fullName,
            ["title"] = Str(pr, "title"),
            ["state"] = Str(pr, "state"),
            ["draft"] = Bool(pr, "draft"),
            ["author_id"] = UserId(user),
            ["author_login"] = Str(user, "login"),
            ["base_ref"] = Str(baseRef, "ref"),
            ["head_ref"] = Str(head, "ref"),
            ["head_repo"] = Str(Object(head, "repo"), "full_name"),
            ["created_at"] = Iso(pr, "created_at"),
            ["updated_at"] = Iso(pr, "updated_at"),
            ["closed_at"] = Iso(pr, "closed_at"),
            ["merged_at"] = Iso(pr, "merged_at"),
            ["merge_commit_sha"] = Str(pr, "merge_commit_sha"),
            ["comment_count"] = Num(pr, "comments"),
            ["review_comment_count"] = Num(pr, "review_comments"),
            ["additions"] = Num(pr, "additions"),
            ["deletions"] = Num(pr, "deletions"),
            ["changed_files"] = Num(pr, "changed_files"),
            ["commit_count"] = Num(pr, "commits"),
        };

        return node.ToJsonString();
    }

    private static RawFact RepositoryFact(JsonElement repo) =>
        new(Source, "repository", Id(repo), repo.GetRawText(), GetTimestamp(repo, "updated_at"));

    /// <summary>
    /// Flattens a GitHub PR review into the fields Factarium uses: identity (id) and
    /// provenance (repo + PR number), the reviewer, verdict state, when it was
    /// submitted, and the reviewed commit. The nested user blob and body are discarded.
    /// </summary>
    private static string FlattenReview(JsonElement review, string fullName, int number)
    {
        var user = Object(review, "user");

        var node = new JsonObject
        {
            ["id"] = Num(review, "id"),
            ["repository_full_name"] = fullName,
            ["pull_request_number"] = number,
            ["reviewer_id"] = UserId(user),
            ["reviewer_login"] = Str(user, "login"),
            ["state"] = Str(review, "state"),
            ["submitted_at"] = Iso(review, "submitted_at"),
            ["commit_id"] = Str(review, "commit_id"),
        };

        return node.ToJsonString();
    }

    /// <summary>Commit timestamp: the committer date, falling back to the author date.</summary>
    private static DateTimeOffset? CommitDate(JsonElement commit)
    {
        if (!commit.TryGetProperty("commit", out var inner))
        {
            return null;
        }

        return (inner.TryGetProperty("committer", out var committer) ? GetTimestamp(committer, "date") : null)
               ?? (inner.TryGetProperty("author", out var author) ? GetTimestamp(author, "date") : null);
    }

    /// <summary>
    /// Flattens a GitHub commit into the handful of fields Factarium actually uses:
    /// identity (sha, repo), git history (tree sha + parents), the message, who
    /// authored it and any co-authors, when, and the comment count. GitHub attributes
    /// a commit to its author (not the committer, which is the merge bot on squashed
    /// PRs), so we keep the author: the resolved GitHub account (id/login) when GitHub
    /// matched one, and always the git author name/email as a fallback. Co-authors come
    /// from "Co-authored-by" message trailers. Everything else (embedded user objects,
    /// verification, node ids) is discarded.
    /// </summary>
    private static string FlattenCommit(JsonElement commit, string fullName)
    {
        var inner = Object(commit, "commit");
        var gitAuthor = Object(inner, "author");
        var userAuthor = Object(commit, "author");
        var message = Str(inner, "message");

        var node = new JsonObject
        {
            ["sha"] = Str(commit, "sha"),
            ["repo"] = fullName,
            ["tree_sha"] = Str(Object(inner, "tree"), "sha"),
            ["parents"] = ParentShas(commit),
            ["message"] = message,
            ["author_id"] = UserId(userAuthor),
            ["author_login"] = Str(userAuthor, "login"),
            ["author_name"] = Str(gitAuthor, "name"),
            ["author_email"] = Str(gitAuthor, "email"),
            ["co_authors"] = CoAuthors(message),
            ["committed_at"] = CommitDate(commit)?.ToUniversalTime().ToString("o"),
            ["comment_count"] = CommentCount(inner),
        };

        return node.ToJsonString();
    }

    /// <summary>
    /// Extracts "Co-authored-by: Name &lt;email&gt;" trailers from a commit message
    /// as {name, email} objects, de-duplicated by email (case-insensitive).
    /// </summary>
    private static JsonArray CoAuthors(string? message)
    {
        var array = new JsonArray();
        if (string.IsNullOrEmpty(message))
        {
            return array;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in CoAuthorTrailer.Matches(message))
        {
            var email = match.Groups["email"].Value.Trim();
            if (email.Length == 0 || !seen.Add(email))
            {
                continue;
            }

            array.Add(new JsonObject
            {
                ["name"] = match.Groups["name"].Value.Trim(),
                ["email"] = email,
            });
        }

        return array;
    }

    private static JsonElement Object(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonNode? Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? JsonValue.Create(value.GetInt64())
            : null;

    private static JsonNode? Bool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? JsonValue.Create(value.GetBoolean())
            : null;

    /// <summary>Reads a timestamp property and re-serializes it as a normalized ISO-8601 string.</summary>
    private static string? Iso(JsonElement element, string property) =>
        GetTimestamp(element, property)?.ToUniversalTime().ToString("o");

    private static JsonNode? UserId(JsonElement user) =>
        user.ValueKind == JsonValueKind.Object
        && user.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
            ? JsonValue.Create(id.GetInt64())
            : null;

    private static int CommentCount(JsonElement inner) =>
        inner.ValueKind == JsonValueKind.Object
        && inner.TryGetProperty("comment_count", out var count) && count.ValueKind == JsonValueKind.Number
            ? count.GetInt32()
            : 0;

    private static JsonArray ParentShas(JsonElement commit)
    {
        var array = new JsonArray();
        if (commit.TryGetProperty("parents", out var parents) && parents.ValueKind == JsonValueKind.Array)
        {
            foreach (var parent in parents.EnumerateArray())
            {
                var sha = Str(parent, "sha");
                if (sha is not null)
                {
                    array.Add(sha);
                }
            }
        }

        return array;
    }

    private static string Id(JsonElement element) =>
        element.GetProperty("id").GetInt64().ToString();

    private static DateTimeOffset? GetTimestamp(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetDateTimeOffset()
            : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
