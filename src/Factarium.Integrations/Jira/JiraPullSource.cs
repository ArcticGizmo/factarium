using System.Globalization;
using System.Text.Json;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Jira;

/// <summary>
/// Pull source for Jira Cloud. Bounded to a single project (<see cref="JiraSourceConfig"/>)
/// with a historical <c>SyncSince</c> floor, it replicates four bronze entity types:
/// <c>issue</c> (rich fields incl. story points + sprint), <c>issue_changelog</c> (full
/// history for time-in-status and scope changes), <c>sprint</c> (derived from the issues'
/// Sprint field, so no Jira Software/Agile scope is needed), and <c>issue_devlinks</c>
/// (best-effort PR/branch links). An updated-time cursor makes runs after the first incremental.
/// </summary>
public sealed class JiraPullSource(JiraApiClient client, ILogger<JiraPullSource> logger) : IPullSource
{
    private const string Source = "jira";
    private const string CursorKey = "issues:updated";
    private const string StoryPointsFieldKey = "field:storypoints";
    private const string SprintFieldKey = "field:sprint";
    private const string CloudIdKey = "meta:cloudId";

    private static readonly string[] BaseFields =
        ["summary", "status", "issuetype", "assignee", "created", "updated", "resolutiondate", "project", "parent"];

    public string Type => Source;

    public async Task<SyncResult> PullAsync(SyncContext context, CancellationToken cancellationToken)
    {
        if (context.Config is not JiraSourceConfig config)
        {
            return SyncResult.Failed("Jira integration is misconfigured (expected Jira configuration).");
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Email))
        {
            return SyncResult.Failed("Jira integration requires a site URL and an account email.");
        }

        if (string.IsNullOrWhiteSpace(config.ProjectKey))
        {
            return SyncResult.Failed("Jira integration requires a project key.");
        }

        if (string.IsNullOrWhiteSpace(context.Credential))
        {
            return SyncResult.Failed("Jira integration requires an API token credential.");
        }

        var email = config.Email;
        var token = context.Credential;
        var projectKey = config.ProjectKey;

        // Scoped tokens go through the Atlassian API gateway (site cloud id resolved once
        // and cached); classic tokens hit the site directly.
        var apiRoot = config.BaseUrl.TrimEnd('/');
        if (config.ScopedToken)
        {
            var cloudId = context.Cursor.Get(CloudIdKey);
            if (string.IsNullOrWhiteSpace(cloudId))
            {
                cloudId = await client.ResolveCloudIdAsync(config.BaseUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(cloudId))
                {
                    return SyncResult.Failed("Could not resolve the Jira cloud id for scoped-token access.");
                }

                context.Cursor.Set(CloudIdKey, cloudId);
            }

            apiRoot = $"https://api.atlassian.com/ex/jira/{cloudId}";
        }

        var (storyPointsFieldId, sprintFieldId) = await ResolveFieldIdsAsync(context, apiRoot, email, token, cancellationToken);

        // The floor is the later of the configured history bound and the incremental cursor.
        var lastCursor = ParseDate(context.Cursor.Get(CursorKey));
        var floor = Latest(config.SyncSince, lastCursor);
        var jql = BuildJql(projectKey, floor);

        var fields = BaseFields
            .Append(storyPointsFieldId)
            .Append(sprintFieldId)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .ToArray();

        var facts = new List<RawFact>();
        var issueIds = new List<string>();
        // Sprints are derived from the issues' Sprint field (deduped by id), which avoids
        // the Agile API and its Jira Software scope that scoped tokens can't grant.
        var sprints = new Dictionary<string, JsonElement>();
        DateTimeOffset? maxUpdated = floor;

        await foreach (var issue in client.SearchIssuesAsync(apiRoot, jql, fields, email, token, cancellationToken))
        {
            var id = issue.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            if (id is null)
            {
                continue;
            }

            var hasFields = issue.TryGetProperty("fields", out var f);
            var updated = hasFields ? ParseDateElement(f, "updated") : null;
            facts.Add(new RawFact(Source, "issue", id, issue.GetRawText(), updated));
            issueIds.Add(id);

            if (hasFields && sprintFieldId is not null)
            {
                CollectSprints(f, sprintFieldId, sprints);
            }

            if (updated is not null && (maxUpdated is null || updated > maxUpdated))
            {
                maxUpdated = updated;
            }
        }

        // Full changelog for each changed issue (accurate time-in-status and sprint scope changes).
        foreach (var id in issueIds)
        {
            facts.Add(await BuildChangelogFactAsync(apiRoot, id, email, token, cancellationToken));
        }

        // Dev-links come from an internal endpoint that scoped tokens can't reach, so only
        // attempt it for classic tokens. Best-effort even then: circuit-break on first failure.
        if (config.ScopedToken)
        {
            logger.LogInformation("Scoped-token mode: skipping dev-status PR links (not available via the API gateway)");
        }
        else
        {
            var devLinksAvailable = true;
            foreach (var id in issueIds)
            {
                if (!devLinksAvailable)
                {
                    break;
                }

                try
                {
                    var dev = await client.GetIssueDevLinksAsync(apiRoot, id, email, token, cancellationToken);
                    if (dev is not null)
                    {
                        facts.Add(new RawFact(Source, "issue_devlinks", id, dev.Value.GetRawText(), DevLinksUpdatedAt(dev.Value)));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Jira dev-status unavailable; skipping dev-links for the rest of this run");
                    devLinksAvailable = false;
                }
            }
        }

        facts.AddRange(BuildSprintFacts(sprints, config.SyncSince));

        var written = await context.Sink.WriteAsync(context.IntegrationId, facts, cancellationToken);
        if (maxUpdated is not null && maxUpdated != lastCursor)
        {
            context.Cursor.Set(CursorKey, maxUpdated.Value.ToUniversalTime().ToString("o"));
        }

        logger.LogInformation(
            "Jira sync wrote {Count} records ({Issues} issues) for project {Project}",
            written, issueIds.Count, projectKey);
        return SyncResult.Ok(written);
    }

    private async Task<(string? StoryPoints, string? Sprint)> ResolveFieldIdsAsync(
        SyncContext context, string apiRoot, string email, string token, CancellationToken cancellationToken)
    {
        var storyPoints = context.Cursor.Get(StoryPointsFieldKey);
        var sprint = context.Cursor.Get(SprintFieldKey);
        if (!string.IsNullOrWhiteSpace(storyPoints) && !string.IsNullOrWhiteSpace(sprint))
        {
            return (storyPoints, sprint);
        }

        var (discoveredStoryPoints, discoveredSprint) = await client.DiscoverFieldIdsAsync(apiRoot, email, token, cancellationToken);
        storyPoints ??= discoveredStoryPoints;
        sprint ??= discoveredSprint;

        if (!string.IsNullOrWhiteSpace(storyPoints))
        {
            context.Cursor.Set(StoryPointsFieldKey, storyPoints);
        }

        if (!string.IsNullOrWhiteSpace(sprint))
        {
            context.Cursor.Set(SprintFieldKey, sprint);
        }

        return (storyPoints, sprint);
    }

    private async Task<RawFact> BuildChangelogFactAsync(
        string apiRoot, string issueId, string email, string token, CancellationToken cancellationToken)
    {
        var histories = new List<JsonElement>();
        DateTimeOffset? latest = null;

        await foreach (var entry in client.GetIssueChangelogAsync(apiRoot, issueId, email, token, cancellationToken))
        {
            histories.Add(entry);
            var created = ParseDateElement(entry, "created");
            if (created is not null && (latest is null || created > latest))
            {
                latest = created;
            }
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["issueId"] = issueId,
            ["histories"] = histories,
        });

        return new RawFact(Source, "issue_changelog", issueId, payload, latest);
    }

    // Accumulates the distinct sprint objects embedded in an issue's Sprint field. Modern
    // Jira returns full objects here (id, name, state, dates, boardId); legacy toString
    // entries (non-object) are skipped.
    private static void CollectSprints(JsonElement fields, string sprintFieldId, Dictionary<string, JsonElement> into)
    {
        if (!fields.TryGetProperty(sprintFieldId, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var sprint in value.EnumerateArray())
        {
            if (sprint.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = sprint.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.Number
                ? idValue.GetInt64().ToString(CultureInfo.InvariantCulture)
                : null;
            if (id is not null)
            {
                into.TryAdd(id, sprint.Clone());
            }
        }
    }

    private static List<RawFact> BuildSprintFacts(Dictionary<string, JsonElement> sprints, DateTimeOffset? syncSince)
    {
        var facts = new List<RawFact>();
        foreach (var (id, sprint) in sprints)
        {
            // A sprint's effective end is its completion, else planned end, else start.
            var endish = ParseDateElement(sprint, "completeDate")
                         ?? ParseDateElement(sprint, "endDate")
                         ?? ParseDateElement(sprint, "startDate");

            // Skip sprints that finished before the history floor.
            if (syncSince is not null && endish is not null && endish < syncSince)
            {
                continue;
            }

            facts.Add(new RawFact(Source, "sprint", id, sprint.GetRawText(), endish));
        }

        return facts;
    }

    private static DateTimeOffset? DevLinksUpdatedAt(JsonElement devLinks)
    {
        DateTimeOffset? latest = null;
        if (devLinks.TryGetProperty("pullRequests", out var prs) && prs.ValueKind == JsonValueKind.Array)
        {
            foreach (var pr in prs.EnumerateArray())
            {
                var updated = ParseDateElement(pr, "lastUpdate");
                if (updated is not null && (latest is null || updated > latest))
                {
                    latest = updated;
                }
            }
        }

        return latest;
    }

    private static string BuildJql(string projectKey, DateTimeOffset? floor)
    {
        var clauses = new List<string> { $"project = \"{projectKey}\"" };
        if (floor is not null)
        {
            clauses.Add($"updated >= \"{floor.Value.ToUniversalTime():yyyy-MM-dd HH:mm}\"");
        }

        return $"{string.Join(" AND ", clauses)} ORDER BY updated ASC";
    }

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) => (a, b) switch
    {
        (null, null) => null,
        (null, _) => b,
        (_, null) => a,
        _ => a > b ? a : b,
    };

    private static DateTimeOffset? ParseDateElement(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? ParseDate(value.GetString())
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        // Jira returns local offsets (e.g. +10:00); normalize to UTC so every stored
        // timestamp has a zero offset (Postgres timestamptz requires it).
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
