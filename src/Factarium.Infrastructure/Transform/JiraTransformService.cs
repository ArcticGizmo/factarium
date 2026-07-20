using System.Globalization;
using System.Text.Json;
using Factarium.Domain.Canonical;
using Factarium.Domain.People;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Transform;

/// <summary>
/// Turns the flattened Jira issue records (see <c>JiraPullSource.FlattenIssue</c>) into
/// canonical issues. Every attributed account — assignee, reporter (createdBy), primary
/// developer, and the closer derived from the changelog — becomes a "jira" SourceIdentity
/// keyed by accountId. These identities link to the same Person as GitHub identities, giving
/// cross-source attribution.
/// </summary>
internal sealed class JiraTransformService(FactariumDbContext db, TimeProvider clock)
{
    private const string Source = "jira";

    private sealed record Actor(string AccountId, string? DisplayName);

    public async Task<int> TransformAsync(CancellationToken cancellationToken)
    {
        var records = await db.RawRecords
            .Where(r => r.Source == Source && r.EntityType == "issue")
            .ToListAsync(cancellationToken);

        var changelogs = await db.RawRecords
            .Where(r => r.Source == Source && r.EntityType == "issue_changelog")
            .ToListAsync(cancellationToken);

        // Who closed each issue, from the changelog's resolution-set transitions.
        var closers = BuildClosers(changelogs);

        var identities = await EnsureIdentitiesAsync(records, closers.Values, cancellationToken);

        var existing = await db.CanonicalIssues
            .Where(i => i.Source == Source)
            .ToDictionaryAsync(i => i.ExternalId, cancellationToken);

        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;

            if (!existing.TryGetValue(record.SourceId, out var issue))
            {
                issue = new CanonicalIssue
                {
                    Source = Source,
                    ExternalId = record.SourceId,
                    Key = Str(root, "key") ?? record.SourceId,
                };
                db.CanonicalIssues.Add(issue);
                existing[record.SourceId] = issue;
            }

            var closedAt = Date(root, "closed_at");
            var (sprintId, sprintName) = CurrentSprint(root);

            issue.Key = Str(root, "key") ?? issue.Key;
            issue.ProjectKey = Str(root, "project_key");
            issue.Title = Str(root, "title");
            issue.IssueTypeId = Str(root, "issue_type_id");
            issue.IssueType = Str(root, "issue_type");
            issue.Status = Str(root, "status");
            issue.StatusCategory = Str(root, "status_category");
            issue.IsClosed = closedAt is not null || Str(root, "status_category_key") == "done";
            issue.StoryPoints = Number(root, "story_points");
            issue.CommentCount = Int(root, "comment_count");
            issue.CreatedAt = Date(root, "created_at");
            issue.UpdatedAt = Date(root, "updated_at");
            issue.ClosedAt = closedAt;
            issue.SprintId = sprintId;
            issue.SprintName = sprintName;

            AssignActor(root, "assignee", identities,
                (login, id) => { issue.AssigneeLogin = login; issue.AssigneeIdentityId = id; });
            AssignActor(root, "reporter", identities,
                (login, id) => { issue.ReporterLogin = login; issue.ReporterIdentityId = id; });
            AssignActor(root, "primary_developer", identities,
                (login, id) => { issue.PrimaryDeveloperLogin = login; issue.PrimaryDeveloperIdentityId = id; });

            var closer = closers.TryGetValue(record.SourceId, out var c) ? c.AccountId : null;
            issue.ClosedByLogin = closer;
            issue.ClosedByIdentityId = Identity(closer, identities);
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    // Sets the login + resolved identity for a "{prefix}_id"/"{prefix}_name" account on the issue.
    private static void AssignActor(
        JsonElement root, string prefix, IReadOnlyDictionary<string, Guid> identities, Action<string?, Guid?> set)
    {
        var accountId = Str(root, $"{prefix}_id");
        set(accountId, Identity(accountId, identities));
    }

    private static Guid? Identity(string? accountId, IReadOnlyDictionary<string, Guid> identities) =>
        accountId is not null && identities.TryGetValue(accountId, out var id) ? id : null;

    /// <summary>
    /// The issue's current sprint: the one in the "active" state, else the most recent by
    /// start date. Returns (null, null) when the issue has no sprints.
    /// </summary>
    private static (long? Id, string? Name) CurrentSprint(JsonElement root)
    {
        if (!root.TryGetProperty("sprints", out var sprints) || sprints.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        JsonElement? best = null;
        DateTimeOffset? bestStart = null;
        foreach (var sprint in sprints.EnumerateArray())
        {
            if (sprint.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // An active sprint always wins.
            if (string.Equals(Str(sprint, "state"), "active", StringComparison.OrdinalIgnoreCase))
            {
                return (Long(sprint, "id"), Str(sprint, "name"));
            }

            var start = Date(sprint, "start_date");
            if (best is null || (start is not null && (bestStart is null || start > bestStart)))
            {
                best = sprint;
                bestStart = start;
            }
        }

        return best is null ? (null, null) : (Long(best.Value, "id"), Str(best.Value, "name"));
    }

    /// <summary>
    /// Maps issueId → the account that closed it, taken from the latest changelog history that
    /// sets a resolution (Jira records a "resolution" field change when an issue is resolved).
    /// </summary>
    private static Dictionary<string, Actor> BuildClosers(List<RawRecord> changelogs)
    {
        var closers = new Dictionary<string, Actor>();

        foreach (var record in changelogs)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;
            var issueId = Str(root, "issueId") ?? record.SourceId;
            if (!root.TryGetProperty("histories", out var histories) || histories.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            Actor? closer = null;
            DateTimeOffset? closedAt = null;
            foreach (var history in histories.EnumerateArray())
            {
                if (!SetsResolution(history))
                {
                    continue;
                }

                var created = Date(history, "created");
                if (closer is null || (created is not null && (closedAt is null || created > closedAt)))
                {
                    var author = history.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object
                        ? new Actor(Str(a, "accountId") ?? string.Empty, Str(a, "displayName"))
                        : null;
                    if (author is { AccountId.Length: > 0 })
                    {
                        closer = author;
                        closedAt = created;
                    }
                }
            }

            if (closer is not null)
            {
                closers[issueId] = closer;
            }
        }

        return closers;
    }

    // True when a history entry contains a "resolution" item transitioning to a non-empty value.
    private static bool SetsResolution(JsonElement history)
    {
        if (!history.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (string.Equals(Str(item, "field"), "resolution", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(Str(item, "to")))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Dictionary<string, Guid>> EnsureIdentitiesAsync(
        List<RawRecord> records, IEnumerable<Actor> closers, CancellationToken cancellationToken)
    {
        var existing = await db.SourceIdentities
            .Where(i => i.Source == Source)
            .ToListAsync(cancellationToken);

        var map = existing.ToDictionary(i => i.Login, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var discovered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;
            Collect(discovered, Str(root, "assignee_id"), Str(root, "assignee_name"));
            Collect(discovered, Str(root, "reporter_id"), Str(root, "reporter_name"));
            Collect(discovered, Str(root, "primary_developer_id"), Str(root, "primary_developer_name"));
        }

        foreach (var closer in closers)
        {
            Collect(discovered, closer.AccountId, closer.DisplayName);
        }

        var now = clock.GetUtcNow();
        foreach (var (accountId, displayName) in discovered)
        {
            if (map.ContainsKey(accountId))
            {
                continue;
            }

            var identity = new SourceIdentity
            {
                Id = Guid.NewGuid(),
                Source = Source,
                Login = accountId,
                DisplayName = displayName ?? accountId,
                PersonId = null,
                FirstSeenAt = now,
            };
            db.SourceIdentities.Add(identity);
            map[accountId] = identity.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return map;
    }

    private static void Collect(Dictionary<string, string?> into, string? accountId, string? displayName)
    {
        if (!string.IsNullOrEmpty(accountId))
        {
            into[accountId] = displayName;
        }
    }

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Int(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static double? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static long? Long(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        // Flattened payloads store UTC ISO-8601 strings; normalize defensively anyway.
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
