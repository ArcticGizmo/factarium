using System.Globalization;
using System.Text.Json;
using Factarium.Domain.Canonical;
using Factarium.Domain.People;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Transform;

/// <summary>
/// Turns raw Jira issue records into canonical issues, creating a "jira"
/// SourceIdentity per assignee (accountId). These identities link to the same
/// Person as GitHub identities, giving cross-source attribution.
/// </summary>
internal sealed class JiraTransformService(FactariumDbContext db, TimeProvider clock)
{
    private const string Source = "jira";

    public async Task<int> TransformAsync(CancellationToken cancellationToken)
    {
        var records = await db.RawRecords
            .Where(r => r.Source == Source && r.EntityType == "issue")
            .ToListAsync(cancellationToken);

        var identities = await EnsureIdentitiesAsync(records, cancellationToken);

        var existing = await db.CanonicalIssues
            .Where(i => i.Source == Source)
            .ToDictionaryAsync(i => i.ExternalId, cancellationToken);

        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;
            var fields = root.TryGetProperty("fields", out var f) ? f : default;

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

            var (accountId, _) = Assignee(fields);
            var resolvedAt = Date(fields, "resolutiondate");

            issue.Key = Str(root, "key") ?? issue.Key;
            issue.ProjectKey = fields.TryGetProperty("project", out var p) ? Str(p, "key") : null;
            issue.IssueType = fields.TryGetProperty("issuetype", out var it) ? Str(it, "name") : null;
            issue.Status = fields.TryGetProperty("status", out var st) ? Str(st, "name") : null;
            issue.ResolvedAt = resolvedAt;
            issue.IsResolved = resolvedAt is not null || StatusCategoryIsDone(fields);
            issue.CreatedAt = Date(fields, "created");
            issue.UpdatedAt = Date(fields, "updated");
            issue.AssigneeLogin = accountId;
            issue.AssigneeIdentityId = accountId is not null && identities.TryGetValue(accountId, out var id) ? id : null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private async Task<Dictionary<string, Guid>> EnsureIdentitiesAsync(
        List<RawRecord> records, CancellationToken cancellationToken)
    {
        var existing = await db.SourceIdentities
            .Where(i => i.Source == Source)
            .ToListAsync(cancellationToken);

        var map = existing.ToDictionary(i => i.Login, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var discovered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var fields = doc.RootElement.TryGetProperty("fields", out var f) ? f : default;
            var (accountId, displayName) = Assignee(fields);
            if (accountId is not null)
            {
                discovered[accountId] = displayName;
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

    private static (string? AccountId, string? DisplayName) Assignee(JsonElement fields)
    {
        if (fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("assignee", out var assignee)
            && assignee.ValueKind == JsonValueKind.Object)
        {
            return (Str(assignee, "accountId"), Str(assignee, "displayName"));
        }

        return (null, null);
    }

    private static bool StatusCategoryIsDone(JsonElement fields) =>
        fields.ValueKind == JsonValueKind.Object
        && fields.TryGetProperty("status", out var status)
        && status.TryGetProperty("statusCategory", out var category)
        && Str(category, "key") == "done";

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        // Jira returns local offsets; store UTC so canonical timestamps match the bronze
        // tier and satisfy Postgres timestamptz (zero-offset only).
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
