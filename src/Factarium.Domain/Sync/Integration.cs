namespace Factarium.Domain.Sync;

/// <summary>
/// A configured connection to a data source (e.g. a GitHub org). Holds schedule,
/// encrypted credential, source-specific settings, incremental cursor state, and
/// the outcome of the last run. The scheduler and the management UI both treat
/// this row as the source of truth.
/// </summary>
public class Integration
{
    public Guid Id { get; set; }

    /// <summary>Source type discriminator, e.g. "github".</summary>
    public required string Type { get; set; }

    /// <summary>Human-friendly unique name.</summary>
    public required string Name { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Quartz cron expression; null means manual-trigger only.</summary>
    public string? ScheduleCron { get; set; }

    /// <summary>Data Protection-encrypted credential (e.g. a PAT).</summary>
    public string? EncryptedCredential { get; set; }

    /// <summary>Source-specific settings as JSON (e.g. { "org": "acme" }).</summary>
    public string? SettingsJson { get; set; }

    /// <summary>Per-entity incremental watermarks as JSON.</summary>
    public string? CursorState { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastRunStartedAt { get; set; }

    public DateTimeOffset? LastRunCompletedAt { get; set; }

    public SyncRunStatus LastRunStatus { get; set; } = SyncRunStatus.Never;

    public string? LastRunError { get; set; }

    public int LastRunRecordsWritten { get; set; }
}

public enum SyncRunStatus
{
    Never = 0,
    Running = 1,
    Success = 2,
    Failed = 3,
}
