namespace Factarium.Domain.Sync;

/// <summary>
/// One recorded execution of an integration's sync. Where <see cref="Integration"/>
/// keeps only the latest outcome (for the connection's own page), a SyncRun is the
/// durable history: every scheduled (background) or manual (adhoc) run appends a row,
/// so the Sync Activity view can show what is running, what has run, and what errored.
/// The integration's name and type are snapshotted here so the log reads correctly
/// even after a rename and needs no join to render.
/// </summary>
public class SyncRun
{
    public long Id { get; set; }

    public Guid IntegrationId { get; set; }

    /// <summary>Integration name as it was at run time.</summary>
    public required string IntegrationName { get; set; }

    /// <summary>Integration type discriminator (e.g. "github") at run time.</summary>
    public required string IntegrationType { get; set; }

    /// <summary>
    /// The entity this run synced (e.g. "issue", "commit"). Each entity runs and is
    /// recorded independently. Null only when a run failed before any entity ran (e.g.
    /// no pull source is registered for the integration type).
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>Whether this run was fired by the schedule or triggered by hand.</summary>
    public SyncRunTrigger Trigger { get; set; }

    /// <summary>
    /// Reuses <see cref="SyncRunStatus"/>; a run is only ever
    /// <see cref="SyncRunStatus.Running"/>, <see cref="SyncRunStatus.Success"/>, or
    /// <see cref="SyncRunStatus.Failed"/> (never <see cref="SyncRunStatus.Never"/>).
    /// </summary>
    public SyncRunStatus Status { get; set; } = SyncRunStatus.Running;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int RecordsWritten { get; set; }

    public string? Error { get; set; }
}

/// <summary>How a <see cref="SyncRun"/> was initiated.</summary>
public enum SyncRunTrigger
{
    /// <summary>Fired by the integration's cron schedule.</summary>
    Scheduled = 0,

    /// <summary>Triggered by hand via the "Run now" action.</summary>
    Manual = 1,
}
