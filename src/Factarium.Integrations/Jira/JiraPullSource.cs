using System.Globalization;
using System.Text.Json;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Jira;

/// <summary>
/// Pull source for Jira Cloud. Bounded to a single project (<see cref="JiraSourceConfig"/>)
/// with a historical <c>SyncSince</c> floor, it runs four independent entities:
/// <c>issue</c> (rich fields incl. story points + sprint, fetched from the API), and three
/// derived from the replicated issues — <c>sprint</c> (from the issues' Sprint field, so no
/// Jira Software/Agile scope is needed), <c>issue_changelog</c> (full history for
/// time-in-status and scope changes), and <c>issue_devlinks</c> (best-effort PR/branch
/// links, classic tokens only). Each entity keeps its own updated-time cursor and commits
/// independently, so a failure in one never discards another's records.
/// </summary>
public sealed class JiraPullSource(JiraApiClient client, ILogger<JiraPullSource> logger) : IPullSource
{
    private const string Source = "jira";
    private const string CursorKey = "issues:updated";
    private const string SprintCursorKey = "sprint:since";
    private const string ChangelogCursorKey = "changelog:since";
    private const string DevLinksCursorKey = "devlinks:since";
    private const string StoryPointsFieldKey = "field:storypoints";
    private const string SprintFieldKey = "field:sprint";
    private const string CloudIdKey = "meta:cloudId";

    // Records committed (and cursor advanced) every this many, so a mid-entity failure
    // leaves prior batches persisted and resumable.
    private const int BatchSize = 200;

    private static readonly string[] BaseFields =
        ["summary", "status", "issuetype", "assignee", "created", "updated", "resolutiondate", "project", "parent"];

    public string Type => Source;

    // issue first: sprint/changelog/devlinks read the issues it replicates to bronze.
    public IReadOnlyList<string> Entities { get; } = ["issue", "sprint", "issue_changelog", "issue_devlinks"];

    public IReadOnlyList<string> CursorKeyPrefixesForEntity(string entityType) => entityType switch
    {
        "issue" => [CursorKey],
        "sprint" => [SprintCursorKey],
        "issue_changelog" => [ChangelogCursorKey],
        "issue_devlinks" => [DevLinksCursorKey],
        _ => [],
    };

    public async Task<SyncResult> PullEntityAsync(string entity, SyncContext context, CancellationToken cancellationToken)
    {
        var (prep, failure) = await PrepareAsync(context, cancellationToken);
        if (prep is null)
        {
            return failure!;
        }

        return entity switch
        {
            "issue" => await SyncIssuesAsync(context, prep, cancellationToken),
            "sprint" => await SyncSprintsAsync(context, prep, cancellationToken),
            "issue_changelog" => await SyncChangelogsAsync(context, prep, cancellationToken),
            "issue_devlinks" => await SyncDevLinksAsync(context, prep, cancellationToken),
            _ => SyncResult.Ok(0),
        };
    }

    // Config validation + gateway/cloud-id resolution + field-id discovery, shared by every
    // entity. Cheap on repeat calls (cloud id and field ids are cached in the cursor).
    private async Task<(JiraPrep? Prep, SyncResult? Failure)> PrepareAsync(
        SyncContext context, CancellationToken cancellationToken)
    {
        if (context.Config is not JiraSourceConfig config)
        {
            return (null, SyncResult.Failed("Jira integration is misconfigured (expected Jira configuration)."));
        }

        if (string.IsNullOrWhiteSpace(config.BaseUrl) || string.IsNullOrWhiteSpace(config.Email))
        {
            return (null, SyncResult.Failed("Jira integration requires a site URL and an account email."));
        }

        if (string.IsNullOrWhiteSpace(config.ProjectKey))
        {
            return (null, SyncResult.Failed("Jira integration requires a project key."));
        }

        if (string.IsNullOrWhiteSpace(context.Credential))
        {
            return (null, SyncResult.Failed("Jira integration requires an API token credential."));
        }

        var email = config.Email;
        var token = context.Credential;

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
                    return (null, SyncResult.Failed("Could not resolve the Jira cloud id for scoped-token access."));
                }

                context.Cursor.Set(CloudIdKey, cloudId);
            }

            apiRoot = $"https://api.atlassian.com/ex/jira/{cloudId}";
        }

        var (storyPointsFieldId, sprintFieldId) = await ResolveFieldIdsAsync(context, apiRoot, email, token, cancellationToken);
        return (new JiraPrep(config, apiRoot, email, token, storyPointsFieldId, sprintFieldId), null);
    }

    private sealed record JiraPrep(
        JiraSourceConfig Config,
        string ApiRoot,
        string Email,
        string Token,
        string? StoryPointsFieldId,
        string? SprintFieldId);

    // "issue": fetch changed issues from the API and write them, advancing issues:updated.
    private async Task<SyncResult> SyncIssuesAsync(SyncContext context, JiraPrep prep, CancellationToken cancellationToken)
    {
        var lastCursor = ParseDate(context.Cursor.Get(CursorKey));
        var floor = Latest(prep.Config.SyncSince, lastCursor);
        var jql = BuildJql(prep.Config.ProjectKey!, floor);
        var fields = BuildFields(prep);

        var batch = new List<RawFact>();
        var written = 0;
        DateTimeOffset? maxUpdated = null;

        await foreach (var issue in client.SearchIssuesAsync(prep.ApiRoot, jql, fields, prep.Email, prep.Token, cancellationToken))
        {
            var id = issue.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            if (id is null)
            {
                continue;
            }

            var updated = issue.TryGetProperty("fields", out var f) ? ParseDateElement(f, "updated") : null;
            batch.Add(new RawFact(Source, "issue", id, issue.GetRawText(), updated));
            if (updated is not null && (maxUpdated is null || updated > maxUpdated))
            {
                maxUpdated = updated;
            }

            if (batch.Count >= BatchSize)
            {
                written += await FlushAsync(context, batch, CursorKey, maxUpdated, cancellationToken);
            }
        }

        written += await FlushAsync(context, batch, CursorKey, maxUpdated, cancellationToken);
        logger.LogInformation("Jira issue sync wrote {Count} for project {Project}", written, prep.Config.ProjectKey);
        return SyncResult.Ok(written);
    }

    // "sprint": derive sprints from the Sprint field of issues already in bronze. No API call.
    private async Task<SyncResult> SyncSprintsAsync(SyncContext context, JiraPrep prep, CancellationToken cancellationToken)
    {
        if (prep.SprintFieldId is null)
        {
            return SyncResult.Ok(0);
        }

        var since = ParseDate(context.Cursor.Get(SprintCursorKey));
        var sprints = new Dictionary<string, JsonElement>();
        DateTimeOffset? mark = null;

        await foreach (var issue in context.Reader.ReadAsync(context.IntegrationId, "issue", since, cancellationToken))
        {
            using (var doc = JsonDocument.Parse(issue.Payload))
            {
                if (doc.RootElement.TryGetProperty("fields", out var fields))
                {
                    CollectSprints(fields, prep.SprintFieldId, sprints);
                }
            }

            mark = issue.SourceUpdatedAt ?? mark;
        }

        var facts = BuildSprintFacts(sprints, prep.Config.SyncSince);
        var written = facts.Count > 0
            ? await context.Sink.WriteAsync(context.IntegrationId, facts, cancellationToken)
            : 0;
        if (mark is not null)
        {
            context.Cursor.Set(SprintCursorKey, mark.Value.ToUniversalTime().ToString("o"));
        }

        logger.LogInformation("Jira sprint sync wrote {Count} for project {Project}", written, prep.Config.ProjectKey);
        return SyncResult.Ok(written);
    }

    // "issue_changelog": fetch the full changelog for each issue in bronze, keyed off its own cursor.
    private async Task<SyncResult> SyncChangelogsAsync(SyncContext context, JiraPrep prep, CancellationToken cancellationToken)
    {
        var since = ParseDate(context.Cursor.Get(ChangelogCursorKey));
        var batch = new List<RawFact>();
        var written = 0;
        DateTimeOffset? mark = null;

        await foreach (var issue in context.Reader.ReadAsync(context.IntegrationId, "issue", since, cancellationToken))
        {
            batch.Add(await BuildChangelogFactAsync(prep.ApiRoot, issue.SourceId, prep.Email, prep.Token, cancellationToken));
            mark = issue.SourceUpdatedAt ?? mark;

            if (batch.Count >= BatchSize)
            {
                written += await FlushAsync(context, batch, ChangelogCursorKey, mark, cancellationToken);
            }
        }

        written += await FlushAsync(context, batch, ChangelogCursorKey, mark, cancellationToken);
        logger.LogInformation("Jira changelog sync wrote {Count} for project {Project}", written, prep.Config.ProjectKey);
        return SyncResult.Ok(written);
    }

    // "issue_devlinks": PR/branch links per issue (classic tokens only; the endpoint is
    // internal and not reachable via the scoped-token gateway). Best-effort.
    private async Task<SyncResult> SyncDevLinksAsync(SyncContext context, JiraPrep prep, CancellationToken cancellationToken)
    {
        if (prep.Config.ScopedToken)
        {
            logger.LogInformation("Scoped-token mode: skipping dev-status PR links (not available via the API gateway)");
            return SyncResult.Ok(0);
        }

        var since = ParseDate(context.Cursor.Get(DevLinksCursorKey));
        var batch = new List<RawFact>();
        var written = 0;
        DateTimeOffset? mark = null;

        await foreach (var issue in context.Reader.ReadAsync(context.IntegrationId, "issue", since, cancellationToken))
        {
            JsonElement? dev;
            try
            {
                dev = await client.GetIssueDevLinksAsync(prep.ApiRoot, issue.SourceId, prep.Email, prep.Token, cancellationToken);
            }
            catch (Exception ex)
            {
                // Unsupported endpoint / no dev-tools permission: treat as "no links" and stop,
                // committing what we have rather than failing the whole entity.
                logger.LogWarning(ex, "Jira dev-status unavailable; stopping dev-links for this run");
                break;
            }

            if (dev is not null)
            {
                batch.Add(new RawFact(Source, "issue_devlinks", issue.SourceId, dev.Value.GetRawText(), DevLinksUpdatedAt(dev.Value)));
            }

            mark = issue.SourceUpdatedAt ?? mark;
            if (batch.Count >= BatchSize)
            {
                written += await FlushAsync(context, batch, DevLinksCursorKey, mark, cancellationToken);
            }
        }

        written += await FlushAsync(context, batch, DevLinksCursorKey, mark, cancellationToken);
        return SyncResult.Ok(written);
    }

    // Writes the accumulated batch (if any) and advances the entity's cursor to the watermark,
    // then clears the batch. Returns the number written.
    private static async Task<int> FlushAsync(
        SyncContext context, List<RawFact> batch, string cursorKey, DateTimeOffset? mark, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            if (mark is not null)
            {
                context.Cursor.Set(cursorKey, mark.Value.ToUniversalTime().ToString("o"));
            }

            return 0;
        }

        var written = await context.Sink.WriteAsync(context.IntegrationId, batch, cancellationToken);
        if (mark is not null)
        {
            context.Cursor.Set(cursorKey, mark.Value.ToUniversalTime().ToString("o"));
        }

        batch.Clear();
        return written;
    }

    private static string[] BuildFields(JiraPrep prep) =>
        BaseFields
            .Append(prep.StoryPointsFieldId)
            .Append(prep.SprintFieldId)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .ToArray();

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
