using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.Jira;

/// <summary>
/// Pull source for Jira Cloud. Bounded to a single project (<see cref="JiraSourceConfig"/>)
/// with a historical <c>SyncSince</c> floor, it runs four independent entities:
/// <c>issue</c> (rich fields incl. story points + sprint, fetched from the API), and three
/// derived from the replicated issues — <c>sprint</c> (from the issues' Sprint field, so no
/// Jira Software/Agile scope is needed), <c>issue_changelog</c> (history filtered to the
/// flow/estimate fields we track — see <see cref="RelevantChangelogFields"/> — for
/// time-in-status, scope, and re-estimation), and <c>issue_devlinks</c> (best-effort PR/branch
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
    private const string PrimaryDevFieldKey = "field:primarydev";
    private const string CloudIdKey = "meta:cloudId";

    // Records committed (and cursor advanced) every this many, so a mid-entity failure
    // leaves prior batches persisted and resumable.
    private const int BatchSize = 200;

    // Requested from the API and kept by FlattenIssue — nothing else is returned. "comment"
    // is the only heavy field (the search endpoint has no count-only option, so we ask for it
    // and keep just comment.total). The story-points, sprint, and primary-developer custom
    // fields are appended per-instance in BuildFields once their ids are discovered.
    private static readonly string[] BaseFields =
        ["summary", "status", "issuetype", "assignee", "reporter", "created", "updated", "resolutiondate", "project", "comment"];

    // The only changed-fields worth replicating from an issue's history: status/assignee drive
    // time-in-stage-per-person, resolution flags reopens, Sprint tracks scope in/out, Flagged
    // marks blocked spans, and story points capture re-estimation. Every other change Jira
    // records — worklogs, ranking, attachments, links, descriptions, comments… — is dropped at
    // fetch time so bronze stays a compact projection rather than a raw history dump. Matched by
    // display name (the ids are custom-field ids that differ per instance); story points are
    // named "Story Points" (company-managed) or "Story point estimate" (team-managed).
    private static readonly HashSet<string> RelevantChangelogFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "assignee", "resolution", "Sprint", "Flagged", "Story Points", "Story point estimate",
    };

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

        var (storyPointsFieldId, sprintFieldId, primaryDevFieldId) =
            await ResolveFieldIdsAsync(context, apiRoot, email, token, cancellationToken);
        return (new JiraPrep(config, apiRoot, email, token, storyPointsFieldId, sprintFieldId, primaryDevFieldId), null);
    }

    private sealed record JiraPrep(
        JiraSourceConfig Config,
        string ApiRoot,
        string Email,
        string Token,
        string? StoryPointsFieldId,
        string? SprintFieldId,
        string? PrimaryDeveloperFieldId);

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
            batch.Add(new RawFact(Source, "issue", id, FlattenIssue(issue, prep), updated));
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
                CollectSprints(doc.RootElement, sprints);
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

    // Writes the accumulated batch (if any), advances the entity's cursor to the watermark, then
    // checkpoints that progress so an interrupted run resumes here (issues are pulled oldest-first,
    // so the cursor safely means "done up to this point"). Returns the number written.
    private static async Task<int> FlushAsync(
        SyncContext context, List<RawFact> batch, string cursorKey, DateTimeOffset? mark, CancellationToken cancellationToken)
    {
        var written = 0;
        if (batch.Count > 0)
        {
            written = await context.Sink.WriteAsync(context.IntegrationId, batch, cancellationToken);
            batch.Clear();
        }

        if (mark is not null)
        {
            context.Cursor.Set(cursorKey, mark.Value.ToUniversalTime().ToString("o"));
        }

        // Persist records first, then the cursor: an interrupt re-fetches at worst, never skips.
        await context.CheckpointAsync(cancellationToken);
        return written;
    }

    private static string[] BuildFields(JiraPrep prep) =>
        BaseFields
            .Append(prep.StoryPointsFieldId)
            .Append(prep.SprintFieldId)
            .Append(prep.PrimaryDeveloperFieldId)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .ToArray();

    private async Task<(string? StoryPoints, string? Sprint, string? PrimaryDev)> ResolveFieldIdsAsync(
        SyncContext context, string apiRoot, string email, string token, CancellationToken cancellationToken)
    {
        var storyPoints = context.Cursor.Get(StoryPointsFieldKey);
        var sprint = context.Cursor.Get(SprintFieldKey);
        var primaryDev = context.Cursor.Get(PrimaryDevFieldKey);
        if (!string.IsNullOrWhiteSpace(storyPoints)
            && !string.IsNullOrWhiteSpace(sprint)
            && !string.IsNullOrWhiteSpace(primaryDev))
        {
            return (storyPoints, sprint, primaryDev);
        }

        var (discoveredStoryPoints, discoveredSprint, discoveredPrimaryDev) =
            await client.DiscoverFieldIdsAsync(apiRoot, email, token, cancellationToken);
        storyPoints ??= discoveredStoryPoints;
        sprint ??= discoveredSprint;
        primaryDev ??= discoveredPrimaryDev;

        if (!string.IsNullOrWhiteSpace(storyPoints))
        {
            context.Cursor.Set(StoryPointsFieldKey, storyPoints);
        }

        if (!string.IsNullOrWhiteSpace(sprint))
        {
            context.Cursor.Set(SprintFieldKey, sprint);
        }

        if (!string.IsNullOrWhiteSpace(primaryDev))
        {
            context.Cursor.Set(PrimaryDevFieldKey, primaryDev);
        }

        return (storyPoints, sprint, primaryDev);
    }

    /// <summary>
    /// Flattens a Jira issue into the tight, root-level projection Factarium stores: identity
    /// (id, key, title), type (id + name), status (workflow name plus the category "main type"
    /// and its key for done-detection), story points, the assignee/reporter/primary-developer
    /// accounts, comment count, lifecycle timestamps, project key, and the sprint objects the
    /// derived "sprint" entity needs. The description (ADF) and every nested API blob are
    /// discarded — mirrors the GitHub flatten step so bronze isn't a raw dump.
    /// </summary>
    private static string FlattenIssue(JsonElement issue, JiraPrep prep)
    {
        var fields = Obj(issue, "fields");
        var status = Obj(fields, "status");
        var statusCategory = Obj(status, "statusCategory");
        var issueType = Obj(fields, "issuetype");
        var project = Obj(fields, "project");

        var node = new JsonObject
        {
            ["id"] = Str(issue, "id"),
            ["key"] = Str(issue, "key"),
            ["title"] = Str(fields, "summary"),
            ["issue_type_id"] = Str(issueType, "id"),
            ["issue_type"] = Str(issueType, "name"),
            ["status"] = Str(status, "name"),
            ["status_category"] = Str(statusCategory, "name"),
            ["status_category_key"] = Str(statusCategory, "key"),
            ["story_points"] = Number(fields, prep.StoryPointsFieldId),
            ["comment_count"] = CommentTotal(fields),
            ["created_at"] = Iso(fields, "created"),
            ["updated_at"] = Iso(fields, "updated"),
            ["closed_at"] = Iso(fields, "resolutiondate"),
            ["project_key"] = Str(project, "key"),
            ["sprints"] = SprintArray(fields, prep.SprintFieldId),
        };

        AddAccount(node, "assignee", Obj(fields, "assignee"));
        AddAccount(node, "reporter", Obj(fields, "reporter"));
        AddAccount(node, "primary_developer", User(fields, prep.PrimaryDeveloperFieldId));

        return node.ToJsonString();
    }

    // Sets {prefix}_id / {prefix}_name from a Jira user object's accountId / displayName.
    private static void AddAccount(JsonObject node, string prefix, JsonElement account)
    {
        node[$"{prefix}_id"] = Str(account, "accountId");
        node[$"{prefix}_name"] = Str(account, "displayName");
    }

    // Projects an issue's Sprint custom field into a compact array of sprint objects. Modern
    // Jira returns full objects here (id, name, state, dates, boardId); legacy toString
    // entries (non-object) are skipped. Empty when the field is absent or undiscovered.
    private static JsonArray SprintArray(JsonElement fields, string? sprintFieldId)
    {
        var array = new JsonArray();
        if (sprintFieldId is null
            || !fields.TryGetProperty(sprintFieldId, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return array;
        }

        foreach (var sprint in value.EnumerateArray())
        {
            if (sprint.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            array.Add(new JsonObject
            {
                ["id"] = LongValue(sprint, "id"),
                ["name"] = Str(sprint, "name"),
                ["state"] = Str(sprint, "state"),
                ["board_id"] = LongValue(sprint, "boardId"),
                ["start_date"] = Iso(sprint, "startDate"),
                ["end_date"] = Iso(sprint, "endDate"),
                ["complete_date"] = Iso(sprint, "completeDate"),
                ["goal"] = Str(sprint, "goal"),
            });
        }

        return array;
    }

    // The Jira issue-search endpoint has no count-only comment option, so we request the
    // "comment" field and keep only its total, discarding the (ADF) bodies here.
    private static int CommentTotal(JsonElement fields) =>
        fields.ValueKind == JsonValueKind.Object
        && fields.TryGetProperty("comment", out var comment)
        && comment.ValueKind == JsonValueKind.Object
        && comment.TryGetProperty("total", out var total)
        && total.ValueKind == JsonValueKind.Number
            ? total.GetInt32()
            : 0;

    private static JsonElement Obj(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    // A custom field whose value is a Jira user object (e.g. "Primary Developer").
    private static JsonElement User(JsonElement fields, string? fieldId) =>
        fieldId is not null ? Obj(fields, fieldId) : default;

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Reads a numeric custom field (e.g. story points, which may be fractional) as a double.
    private static JsonNode? Number(JsonElement element, string? property) =>
        property is not null
        && element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? JsonValue.Create(value.GetDouble())
            : null;

    private static JsonNode? LongValue(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? JsonValue.Create(value.GetInt64())
            : null;

    // Reads a Jira timestamp property and re-serializes it as a normalized UTC ISO-8601 string.
    private static string? Iso(JsonElement element, string property) =>
        ParseDateElement(element, property)?.ToString("o");

    // Replicates an issue's changelog as a compact, flow-focused projection: only the history
    // entries that changed a field in RelevantChangelogFields, each reduced to its timestamp, the
    // author's account, and the relevant changes. The full author blob (avatar urls, email,
    // timezone) and every unrelated field change are discarded.
    private async Task<RawFact> BuildChangelogFactAsync(
        string apiRoot, string issueId, string email, string token, CancellationToken cancellationToken)
    {
        var entries = new JsonArray();
        DateTimeOffset? latest = null;

        await foreach (var history in client.GetIssueChangelogAsync(apiRoot, issueId, email, token, cancellationToken))
        {
            var created = ParseDateElement(history, "created");
            var compact = CompactChangelogEntry(history, created);
            if (compact is null)
            {
                continue;
            }

            entries.Add(compact);
            if (created is not null && (latest is null || created > latest))
            {
                latest = created;
            }
        }

        var payload = new JsonObject
        {
            ["issueId"] = issueId,
            ["entries"] = entries,
        }.ToJsonString();

        return new RawFact(Source, "issue_changelog", issueId, payload, latest);
    }

    // Projects one Jira history entry to { at, author_id, author_name, changes[] }, keeping only
    // the changes to fields we track. Returns null when the entry has no timestamp or changed
    // nothing relevant, so it is dropped from the stored changelog entirely.
    private static JsonObject? CompactChangelogEntry(JsonElement history, DateTimeOffset? created)
    {
        if (created is null
            || !history.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var changes = new JsonArray();
        foreach (var item in items.EnumerateArray())
        {
            var field = Str(item, "field");
            if (field is null || !RelevantChangelogFields.Contains(field))
            {
                continue;
            }

            changes.Add(new JsonObject
            {
                ["field"] = field,
                ["field_id"] = Str(item, "fieldId"),
                ["from"] = Str(item, "from"),
                ["from_str"] = Str(item, "fromString"),
                ["to"] = Str(item, "to"),
                ["to_str"] = Str(item, "toString"),
            });
        }

        if (changes.Count == 0)
        {
            return null;
        }

        var author = Obj(history, "author");
        return new JsonObject
        {
            ["at"] = created.Value.ToString("o"),
            ["author_id"] = Str(author, "accountId"),
            ["author_name"] = Str(author, "displayName"),
            ["changes"] = changes,
        };
    }

    // Accumulates the distinct sprint objects from a flattened issue's root-level "sprints"
    // array (projected by FlattenIssue: id, name, state, dates, boardId, goal).
    private static void CollectSprints(JsonElement issue, Dictionary<string, JsonElement> into)
    {
        if (!issue.TryGetProperty("sprints", out var value) || value.ValueKind != JsonValueKind.Array)
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
            // Skip sprints that haven't started yet: a "future" sprint carries no
            // velocity/throughput signal and only clutters the records.
            if (string.Equals(Str(sprint, "state"), "future", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A sprint's effective end is its completion, else planned end, else start.
            // Keys are the snake_case ones FlattenIssue's SprintArray projects.
            var endish = ParseDateElement(sprint, "complete_date")
                         ?? ParseDateElement(sprint, "end_date")
                         ?? ParseDateElement(sprint, "start_date");

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
