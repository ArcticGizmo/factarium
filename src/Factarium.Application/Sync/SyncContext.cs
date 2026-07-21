namespace Factarium.Application.Sync;

/// <summary>
/// Everything a source needs for one sync run: its integration identity, the
/// decrypted credential, its strongly-typed configuration, a cursor store for
/// incremental watermarks, and the sink to write facts into.
/// </summary>
public sealed class SyncContext
{
    public required Guid IntegrationId { get; init; }

    public required string IntegrationName { get; init; }

    /// <summary>Decrypted credential (e.g. a PAT). Null when the source needs none.</summary>
    public string? Credential { get; init; }

    /// <summary>
    /// Strongly-typed, source-specific configuration. Pull sources cast it to their
    /// own <see cref="SourceConfig"/> subtype; push sources need none, so it is null.
    /// </summary>
    public SourceConfig? Config { get; init; }

    public required ICursorStore Cursor { get; init; }

    public required IRawRecordSink Sink { get; init; }

    /// <summary>Reads back bronze records so a dependent entity can source its work-set.</summary>
    public required IRawRecordReader Reader { get; init; }

    /// <summary>
    /// Persists cursor progress partway through an entity so an interrupted run resumes from the
    /// last checkpoint instead of re-fetching the whole entity. Sources call
    /// <see cref="CheckpointAsync"/> right after a flush — once records are safely written and the
    /// cursor advanced. Only safe for oldest-first feeds (the cursor then means "done up to here").
    /// Null means no-op (e.g. tests, or the end-of-entity save handles it).
    /// </summary>
    public Func<CancellationToken, Task>? Checkpoint { get; init; }

    /// <summary>Persists cursor progress if a checkpoint hook is wired; otherwise a no-op.</summary>
    public Task CheckpointAsync(CancellationToken cancellationToken) =>
        Checkpoint?.Invoke(cancellationToken) ?? Task.CompletedTask;
}

/// <summary>
/// Key/value watermark store for incremental sync (e.g. "prs:acme/repo" -> last
/// updated timestamp). Loaded from and flushed back to the integration's persisted
/// cursor state around a run.
/// </summary>
public interface ICursorStore
{
    string? Get(string key);
    void Set(string key, string? value);
}
