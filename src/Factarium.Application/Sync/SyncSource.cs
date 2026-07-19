namespace Factarium.Application.Sync;

/// <summary>
/// A data source Factarium can replicate from. Concrete sources are either pull
/// (scheduled API polling) or push (inbound events). Both write into the same
/// bronze tier via <see cref="SyncContext.Sink"/>.
/// </summary>
public interface ISyncSource
{
    /// <summary>Stable source type discriminator, e.g. "github", "jira", "claude-otel".</summary>
    string Type { get; }
}

/// <summary>
/// Scheduled, cursor-based source (GitHub, Jira). A source is decomposed into
/// independently-runnable entity units (e.g. "issue", "issue_changelog"): the
/// orchestrator runs each one on its own, committing and recording it separately so a
/// failure in one entity never discards another's records. Entities are listed in
/// dependency order (roots first); dependent entities read their inputs from the bronze
/// tier via <see cref="SyncContext.Reader"/> and keep their own cursor.
/// </summary>
public interface IPullSource : ISyncSource
{
    /// <summary>The entity types this source produces, in dependency order.</summary>
    IReadOnlyList<string> Entities { get; }

    /// <summary>
    /// Fetches and writes one entity's records, advancing that entity's cursor. Returns
    /// the count written (or a failure). Throwing is treated the same as a failed result.
    /// </summary>
    Task<SyncResult> PullEntityAsync(string entity, SyncContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Cursor-key prefixes whose removal makes the given bronze entity type re-replicate
    /// from scratch on the next sync. Used when purging an entity's records so a re-sync
    /// brings the data back. Empty when the entity has no cursor (always re-fetched).
    /// </summary>
    IReadOnlyList<string> CursorKeyPrefixesForEntity(string entityType);
}

/// <summary>
/// Inbound/event-driven source (Claude Code OTEL, webhooks). The contract exists
/// from Phase 1 so the sync layer never needs restructuring when Phase 7 lands;
/// no implementation ships until then.
/// </summary>
public interface IPushSource : ISyncSource
{
    Task<SyncResult> IngestAsync(SyncContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

/// <summary>Outcome of a sync run.</summary>
public sealed record SyncResult(int RecordsWritten, string? Error = null)
{
    public bool Succeeded => Error is null;

    public static SyncResult Ok(int recordsWritten) => new(recordsWritten);
    public static SyncResult Failed(string error, int recordsWritten = 0) => new(recordsWritten, error);
}
