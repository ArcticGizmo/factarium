using System.Globalization;
using System.Text.Json;
using Factarium.Domain.Canonical;
using Factarium.Domain.People;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Transform;

/// <summary>
/// Turns the flattened Tempo worklog records into canonical worklogs. The author is a Jira
/// account, so identities are ensured under the <c>jira</c> source — linking logged effort to
/// the same Person as that account's issues and PRs. Idempotent: re-running upserts by Tempo id.
/// </summary>
internal sealed class TempoTransformService(FactariumDbContext db, TimeProvider clock)
{
    private const string Source = "tempo";
    private const string IdentitySource = "jira";

    public async Task<int> TransformAsync(CancellationToken cancellationToken)
    {
        var records = await db.RawRecords
            .Where(r => r.Source == Source && r.EntityType == "worklog")
            .ToListAsync(cancellationToken);
        if (records.Count == 0)
        {
            return 0;
        }

        var identities = await EnsureIdentitiesAsync(records, cancellationToken);

        var existing = await db.CanonicalWorklogs
            .Where(w => w.Source == Source)
            .ToDictionaryAsync(w => w.ExternalId, cancellationToken);

        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;

            if (!existing.TryGetValue(record.SourceId, out var worklog))
            {
                worklog = new CanonicalWorklog { Source = Source, ExternalId = record.SourceId };
                db.CanonicalWorklogs.Add(worklog);
                existing[record.SourceId] = worklog;
            }

            var authorId = Str(root, "author_id");
            worklog.IssueKey = Str(root, "issue_key");
            worklog.IssueExternalId = Num(root, "issue_id");
            worklog.AuthorLogin = authorId;
            worklog.AuthorIdentityId = authorId is not null && identities.TryGetValue(authorId, out var id) ? id : null;
            worklog.TimeSpentSeconds = Int(root, "time_spent_seconds");
            worklog.BillableSeconds = Int(root, "billable_seconds");
            worklog.WorkDate = Day(root, "work_date");
            worklog.Description = Str(root, "description");
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    // Ensures a jira SourceIdentity for every worklog author (accountId), so effort links to the
    // same Person as that account's Jira issues. Display names come from Jira; unseen authors
    // fall back to the accountId until they appear elsewhere.
    private async Task<Dictionary<string, Guid>> EnsureIdentitiesAsync(
        List<Domain.Sync.RawRecord> records, CancellationToken cancellationToken)
    {
        var existing = await db.SourceIdentities
            .Where(i => i.Source == IdentitySource)
            .ToListAsync(cancellationToken);
        var map = existing.ToDictionary(i => i.Login, i => i.Id, StringComparer.OrdinalIgnoreCase);

        var authors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var authorId = Str(doc.RootElement, "author_id");
            if (!string.IsNullOrWhiteSpace(authorId))
            {
                authors.Add(authorId);
            }
        }

        var now = clock.GetUtcNow();
        foreach (var accountId in authors)
        {
            if (map.ContainsKey(accountId))
            {
                continue;
            }

            var identity = new SourceIdentity
            {
                Id = Guid.NewGuid(),
                Source = IdentitySource,
                Login = accountId,
                DisplayName = accountId,
                PersonId = null,
                FirstSeenAt = now,
            };
            db.SourceIdentities.Add(identity);
            map[accountId] = identity.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return map;
    }

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Num(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64().ToString(CultureInfo.InvariantCulture)
            : null;

    private static int Int(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static DateOnly? Day(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
