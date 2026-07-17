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
/// Scheduled, cursor-based source (GitHub, Jira). Implemented in Phase 1+.
/// </summary>
public interface IPullSource : ISyncSource
{
    Task<SyncResult> PullAsync(SyncContext context, CancellationToken cancellationToken);
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
