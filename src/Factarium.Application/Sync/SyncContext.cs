namespace Factarium.Application.Sync;

/// <summary>
/// Everything a source needs for one sync run: its integration identity, the
/// decrypted credential, integration-specific settings, a cursor store for
/// incremental watermarks, and the sink to write facts into.
/// </summary>
public sealed class SyncContext
{
    public required Guid IntegrationId { get; init; }

    public required string IntegrationName { get; init; }

    /// <summary>Decrypted credential (e.g. a PAT). Null when the source needs none.</summary>
    public string? Credential { get; init; }

    /// <summary>Integration-specific configuration (e.g. org, repo filters).</summary>
    public required IReadOnlyDictionary<string, string?> Settings { get; init; }

    public required ICursorStore Cursor { get; init; }

    public required IRawRecordSink Sink { get; init; }
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
