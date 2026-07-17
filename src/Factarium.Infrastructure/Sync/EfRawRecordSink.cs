using System.Security.Cryptography;
using System.Text;
using Factarium.Application.Sync;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Sync;

/// <summary>
/// Persists raw facts into <c>raw_records</c> with idempotent upsert semantics:
/// unchanged payloads (same content hash) are skipped; changed payloads bump the
/// version and refresh the fetch timestamp.
/// </summary>
internal sealed class EfRawRecordSink(FactariumDbContext db, TimeProvider clock) : IRawRecordSink
{
    public async Task<int> WriteAsync(
        Guid integrationId,
        IReadOnlyCollection<RawFact> facts,
        CancellationToken cancellationToken)
    {
        if (facts.Count == 0)
        {
            return 0;
        }

        var now = clock.GetUtcNow();

        // Collapse duplicates within the batch (last write wins).
        var deduped = facts
            .GroupBy(f => (f.EntityType, f.SourceId))
            .Select(g => g.Last())
            .ToList();

        var entityTypes = deduped.Select(f => f.EntityType).ToHashSet();
        var sourceIds = deduped.Select(f => f.SourceId).ToHashSet();

        var existing = await db.RawRecords
            .Where(r => r.IntegrationId == integrationId
                        && entityTypes.Contains(r.EntityType)
                        && sourceIds.Contains(r.SourceId))
            .ToDictionaryAsync(r => (r.EntityType, r.SourceId), cancellationToken);

        var changed = 0;
        foreach (var fact in deduped)
        {
            var hash = ComputeHash(fact.Payload);

            if (existing.TryGetValue((fact.EntityType, fact.SourceId), out var row))
            {
                if (row.ContentHash == hash)
                {
                    continue; // unchanged since last sync
                }

                row.Payload = fact.Payload;
                row.ContentHash = hash;
                row.SourceUpdatedAt = fact.SourceUpdatedAt;
                row.FetchedAt = now;
                row.Version += 1;
            }
            else
            {
                db.RawRecords.Add(new RawRecord
                {
                    IntegrationId = integrationId,
                    Source = fact.Source,
                    EntityType = fact.EntityType,
                    SourceId = fact.SourceId,
                    Payload = fact.Payload,
                    ContentHash = hash,
                    SourceUpdatedAt = fact.SourceUpdatedAt,
                    FirstSeenAt = now,
                    FetchedAt = now,
                    Version = 1,
                });
            }

            changed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    private static string ComputeHash(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(bytes);
    }
}
