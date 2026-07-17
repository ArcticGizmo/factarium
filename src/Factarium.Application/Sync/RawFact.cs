namespace Factarium.Application.Sync;

/// <summary>
/// One replicated record from a source, as close to the source's own shape as
/// possible. Payload is the raw JSON; transforms (Phase 2) turn these into
/// canonical entities. Identity within a source is (Source, EntityType, SourceId).
/// </summary>
public sealed record RawFact(
    string Source,
    string EntityType,
    string SourceId,
    string Payload,
    DateTimeOffset? SourceUpdatedAt = null);

/// <summary>Sink that persists raw facts into the bronze tier (idempotent upsert).</summary>
public interface IRawRecordSink
{
    /// <summary>Writes a batch of facts for an integration. Returns the number of rows inserted or updated.</summary>
    Task<int> WriteAsync(Guid integrationId, IReadOnlyCollection<RawFact> facts, CancellationToken cancellationToken);
}
