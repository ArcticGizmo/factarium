using System.Globalization;
using System.Text.Json;
using Factarium.Application.Transform;
using Factarium.Domain.Canonical;
using Factarium.Domain.People;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Transform;

/// <summary>
/// Derives flow analytics from the Jira <c>issue_changelog</c> bronze records by replaying each
/// issue's history (see <see cref="IssueFlowReplay"/>): time-in-status attributed to the
/// assignee at the time and blocked spans → <see cref="CanonicalIssueSegment"/>, sprint
/// membership intervals → <see cref="CanonicalIssueSprintMembership"/>, and churn counts written
/// back onto <see cref="CanonicalIssue"/>. Runs after <c>JiraTransformService</c> so the issue
/// rows (and their current-assignee identities) already exist. Full recompute each run.
/// </summary>
internal sealed class JiraChangelogTransformService(FactariumDbContext db, TimeProvider clock)
{
    private const string Source = "jira";

    private sealed record IssueMeta(
        string Key,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? ResolvedAt,
        string? CurrentStatus,
        string? CurrentAssigneeAccountId);

    public async Task<int> TransformAsync(CancellationToken cancellationToken)
    {
        var changelogRecords = await db.RawRecords
            .Where(r => r.Source == Source && r.EntityType == "issue_changelog")
            .ToListAsync(cancellationToken);
        if (changelogRecords.Count == 0)
        {
            return 0;
        }

        var issueRecords = await db.RawRecords
            .Where(r => r.Source == Source && r.EntityType == "issue")
            .ToListAsync(cancellationToken);

        // Issue essentials + status→category/rank maps, built from the flattened issue payloads.
        var issues = new Dictionary<string, IssueMeta>();
        var categoryByStatus = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var rankByStatus = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in issueRecords)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;
            var status = Str(root, "status");
            issues[record.SourceId] = new IssueMeta(
                Str(root, "key") ?? record.SourceId,
                Date(root, "created_at"),
                Date(root, "closed_at"),
                status,
                Str(root, "assignee_id"));
            if (status is not null)
            {
                categoryByStatus[status] = Str(root, "status_category");
                rankByStatus[status] = CategoryRank(Str(root, "status_category_key"));
            }
        }

        // Parse each changelog once into the replay's input shape.
        var entriesByIssue = new Dictionary<string, List<ChangelogEntry>>();
        foreach (var record in changelogRecords)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;
            var issueId = Str(root, "issueId") ?? record.SourceId;
            entriesByIssue[issueId] = ParseEntries(root);
        }

        var identities = await EnsureIdentitiesAsync(issues.Values, entriesByIssue.Values, cancellationToken);

        // Full recompute: clear this source's derived rows, then rebuild.
        await db.CanonicalIssueSegments.Where(s => s.Source == Source).ExecuteDeleteAsync(cancellationToken);
        await db.CanonicalIssueSprintMemberships.Where(s => s.Source == Source).ExecuteDeleteAsync(cancellationToken);

        var canonicalIssues = await db.CanonicalIssues
            .Where(i => i.Source == Source)
            .ToDictionaryAsync(i => i.ExternalId, cancellationToken);

        var now = clock.GetUtcNow();
        var segments = new List<CanonicalIssueSegment>();
        var memberships = new List<CanonicalIssueSprintMembership>();

        foreach (var (issueId, entries) in entriesByIssue)
        {
            if (!issues.TryGetValue(issueId, out var meta) || meta.CreatedAt is null)
            {
                continue; // changelog for an issue we haven't replicated, or no start point
            }

            var input = new IssueFlowInput(
                meta.CreatedAt.Value, meta.ResolvedAt, meta.CurrentStatus, meta.CurrentAssigneeAccountId, entries, now);
            var flow = IssueFlowReplay.Compute(input, s => rankByStatus.GetValueOrDefault(s));

            foreach (var seg in flow.StatusSegments)
            {
                segments.Add(Segment(meta.Key, issueId, "status", seg.Status,
                    categoryByStatus.GetValueOrDefault(seg.Status),
                    seg.AssigneeAccountId, identities, seg.StartedAt, seg.EndedAt, now));
            }

            foreach (var seg in flow.FlaggedSegments)
            {
                segments.Add(Segment(meta.Key, issueId, "flagged", null, null,
                    seg.AssigneeAccountId, identities, seg.StartedAt, seg.EndedAt, now));
            }

            foreach (var span in flow.SprintMemberships)
            {
                memberships.Add(new CanonicalIssueSprintMembership
                {
                    Source = Source,
                    IssueKey = meta.Key,
                    IssueExternalId = issueId,
                    SprintId = span.SprintId,
                    SprintName = span.SprintName,
                    AddedAt = span.AddedAt,
                    RemovedAt = span.RemovedAt,
                });
            }

            if (canonicalIssues.TryGetValue(issueId, out var issue))
            {
                issue.ReopenCount = flow.ReopenCount;
                issue.ReassignmentCount = flow.ReassignmentCount;
                issue.BackflowCount = flow.BackflowCount;
            }
        }

        db.CanonicalIssueSegments.AddRange(segments);
        db.CanonicalIssueSprintMemberships.AddRange(memberships);
        await db.SaveChangesAsync(cancellationToken);
        return segments.Count;
    }

    private CanonicalIssueSegment Segment(
        string key, string issueId, string kind, string? value, string? category,
        string? assigneeAccountId, IReadOnlyDictionary<string, Guid> identities,
        DateTimeOffset startedAt, DateTimeOffset? endedAt, DateTimeOffset now) =>
        new()
        {
            Source = Source,
            IssueKey = key,
            IssueExternalId = issueId,
            Kind = kind,
            Value = value,
            Category = category,
            AssigneeIdentityId = assigneeAccountId is not null && identities.TryGetValue(assigneeAccountId, out var id) ? id : null,
            AssigneeLogin = assigneeAccountId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationSeconds = ((endedAt ?? now) - startedAt).TotalSeconds,
            IsOpen = endedAt is null,
        };

    private static List<ChangelogEntry> ParseEntries(JsonElement root)
    {
        var entries = new List<ChangelogEntry>();
        if (!root.TryGetProperty("histories", out var histories) || histories.ValueKind != JsonValueKind.Array)
        {
            return entries;
        }

        foreach (var history in histories.EnumerateArray())
        {
            var created = Date(history, "created");
            if (created is null)
            {
                continue;
            }

            var author = history.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
            var items = new List<ChangelogItem>();
            if (history.TryGetProperty("items", out var itemArray) && itemArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemArray.EnumerateArray())
                {
                    var field = Str(item, "field");
                    if (field is null)
                    {
                        continue;
                    }

                    items.Add(new ChangelogItem(
                        field, Str(item, "fieldId"), Str(item, "from"), Str(item, "fromString"),
                        Str(item, "to"), Str(item, "toString")));
                }
            }

            entries.Add(new ChangelogEntry(created.Value, Str(author, "accountId"), Str(author, "displayName"), items));
        }

        return entries;
    }

    // Ensures a jira SourceIdentity for every account that was ever the assignee (current
    // assignees are already ensured by JiraTransformService; this covers historical ones).
    private async Task<Dictionary<string, Guid>> EnsureIdentitiesAsync(
        IEnumerable<IssueMeta> issues, IEnumerable<List<ChangelogEntry>> changelogs, CancellationToken cancellationToken)
    {
        var existing = await db.SourceIdentities
            .Where(i => i.Source == Source)
            .ToListAsync(cancellationToken);
        var map = existing.ToDictionary(i => i.Login, i => i.Id, StringComparer.OrdinalIgnoreCase);

        var discovered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            Collect(discovered, issue.CurrentAssigneeAccountId, null);
        }

        foreach (var entries in changelogs)
        {
            foreach (var item in entries.SelectMany(e => e.Items)
                         .Where(i => string.Equals(i.Field, "assignee", StringComparison.OrdinalIgnoreCase)))
            {
                Collect(discovered, item.From, item.FromString);
                Collect(discovered, item.To, item.ToStr);
            }
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
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        // Keep the first non-null display name we see for an account.
        if (!into.TryGetValue(accountId, out var current) || (current is null && displayName is not null))
        {
            into[accountId] = displayName;
        }
    }

    // Jira status categories: "new" (To Do), "indeterminate" (In Progress), "done" (Done).
    private static int? CategoryRank(string? categoryKey) => categoryKey?.ToLowerInvariant() switch
    {
        "new" => 0,
        "indeterminate" => 1,
        "done" => 2,
        _ => null,
    };

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
