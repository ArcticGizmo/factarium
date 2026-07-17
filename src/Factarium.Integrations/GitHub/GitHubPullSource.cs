using System.Text.Json;
using System.Text.Json.Nodes;
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

    public string Type => Source;

    public async Task<SyncResult> PullAsync(SyncContext context, CancellationToken cancellationToken)
    {
        var token = context.Credential;
        var (repositories, written) = await ResolveRepositoriesAsync(context, token, cancellationToken);

        if (repositories.Count == 0)
        {
            return SyncResult.Failed("No repositories resolved. Set 'org' and/or 'repos' in integration settings.");
        }

        foreach (var fullName in repositories)
        {
            written += await SyncPullRequestsAsync(context, fullName, token, cancellationToken);
            written += await SyncCommitsAsync(context, fullName, token, cancellationToken);
        }

        return SyncResult.Ok(written);
    }

    private async Task<(List<string> Repositories, int Written)> ResolveRepositoriesAsync(
        SyncContext context, string? token, CancellationToken cancellationToken)
    {
        var repositories = new List<string>();
        var facts = new List<RawFact>();

        // Explicit repos: settings["repos"] = "owner/name,owner/name2"
        if (context.Settings.TryGetValue("repos", out var reposCsv) && !string.IsNullOrWhiteSpace(reposCsv))
        {
            foreach (var slug in reposCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
        }

        // Org enumeration: settings["org"] = "acme"
        if (context.Settings.TryGetValue("org", out var org) && !string.IsNullOrWhiteSpace(org))
        {
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
            facts.Add(new RawFact(Source, "pull_request", Id(pr),
                Enrich(pr, ("repository_full_name", fullName)), updatedAt));
            if (updatedAt is not null && (maxUpdated is null || updatedAt > maxUpdated))
            {
                maxUpdated = updatedAt;
            }

            await foreach (var review in client.GetPagedAsync(
                               $"repos/{fullName}/pulls/{number}/reviews?per_page=100", token, cancellationToken))
            {
                facts.Add(new RawFact(Source, "review", Id(review),
                    Enrich(review, ("repository_full_name", fullName), ("pull_request_number", number)),
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
            var committedAt = commit.TryGetProperty("commit", out var c)
                              && c.TryGetProperty("author", out var a)
                ? GetTimestamp(a, "date")
                : null;

            facts.Add(new RawFact(Source, "commit", $"{fullName}@{sha}",
                Enrich(commit, ("repository_full_name", fullName)), committedAt));
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

    private static RawFact RepositoryFact(JsonElement repo) =>
        new(Source, "repository", Id(repo), repo.GetRawText(), GetTimestamp(repo, "updated_at"));

    /// <summary>Adds Factarium provenance fields (repo/PR context) to a raw payload.</summary>
    private static string Enrich(JsonElement element, params (string Key, JsonNode? Value)[] fields)
    {
        var node = JsonNode.Parse(element.GetRawText())!.AsObject();
        foreach (var (key, value) in fields)
        {
            node[key] = value;
        }

        return node.ToJsonString();
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
