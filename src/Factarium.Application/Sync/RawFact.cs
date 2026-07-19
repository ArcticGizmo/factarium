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

/// <summary>A stored bronze record, as read back by a dependent entity sync.</summary>
public sealed record RawRecordRef(string SourceId, DateTimeOffset? SourceUpdatedAt, string Payload);

/// <summary>
/// Reads bronze records back so an entity can source its work-set from what earlier
/// entities already replicated (e.g. "issue_changelog" reads recently-changed "issue"
/// records), rather than an in-memory hand-off. This is what makes each entity sync
/// independently retriable.
/// </summary>
public interface IRawRecordReader
{
    /// <summary>
    /// Records for an integration of one entity type whose <see cref="RawFact.SourceUpdatedAt"/>
    /// is after <paramref name="updatedAfter"/> (null = all), ordered by that timestamp ascending.
    /// </summary>
    IAsyncEnumerable<RawRecordRef> ReadAsync(
        Guid integrationId, string entityType, DateTimeOffset? updatedAfter, CancellationToken cancellationToken);
}
