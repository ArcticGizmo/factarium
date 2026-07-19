using System.Runtime.CompilerServices;
using Factarium.Application.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Sync;

/// <summary>
/// Reads bronze <c>raw_records</c> back so a dependent entity sync can source its
/// work-set from what an earlier entity already replicated (e.g. the changelog sync
/// reads recently-changed issues). Streams in <see cref="RawFact.SourceUpdatedAt"/>
/// order so callers can advance a cursor as they go.
/// </summary>
internal sealed class EfRawRecordReader(FactariumDbContext db) : IRawRecordReader
{
    public async IAsyncEnumerable<RawRecordRef> ReadAsync(
        Guid integrationId,
        string entityType,
        DateTimeOffset? updatedAfter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = db.RawRecords
            .AsNoTracking()
            .Where(r => r.IntegrationId == integrationId && r.EntityType == entityType);

        if (updatedAfter is not null)
        {
            query = query.Where(r => r.SourceUpdatedAt > updatedAfter);
        }

        // Materialize before yielding: callers stream this while writing (upserting)
        // through the same DbContext, and a still-open reader on that connection would
        // trip Npgsql's "a command is already in progress". Bounded by the changed-since
        // subset, so buffering it is fine. Order by the source timestamp so callers can
        // watermark on it; FirstSeenAt is a stable tiebreaker.
        var records = await query
            .OrderBy(r => r.SourceUpdatedAt)
            .ThenBy(r => r.FirstSeenAt)
            .Select(r => new RawRecordRef(r.SourceId, r.SourceUpdatedAt, r.Payload))
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            yield return record;
        }
    }
}
