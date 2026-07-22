namespace Factarium.Domain.Sync;

/// <summary>
/// A configured connection to a data source. Holds schedule, encrypted credential,
/// incremental cursor state, and the outcome of the last run. Concrete subclasses
/// (<see cref="GitHubIntegration"/>, <see cref="JiraIntegration"/>,
/// <see cref="ClaudeIntegration"/>) carry the configuration each connector actually
/// needs — verified per connector rather than smeared across one generic settings
/// blob. Persisted table-per-hierarchy, discriminated by <see cref="Type"/>.
/// The scheduler and the management UI both treat this row as the source of truth.
/// </summary>
public abstract class Integration
{
    public Guid Id { get; set; }

    /// <summary>
    /// Source type discriminator, e.g. "github". Set by EF from the concrete type;
    /// do not assign directly.
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>Human-friendly unique name.</summary>
    public required string Name { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Quartz cron expression; null means manual-trigger only.</summary>
    public string? ScheduleCron { get; set; }

    /// <summary>Data Protection-encrypted credential (e.g. a PAT).</summary>
    public string? EncryptedCredential { get; set; }

    /// <summary>Per-entity incremental watermarks as JSON.</summary>
    public string? CursorState { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastRunStartedAt { get; set; }

    public DateTimeOffset? LastRunCompletedAt { get; set; }

    public SyncRunStatus LastRunStatus { get; set; } = SyncRunStatus.Never;

    public string? LastRunError { get; set; }

    public int LastRunRecordsWritten { get; set; }
}

/// <summary>GitHub connection: an org and/or an explicit repo list. Pull-based.</summary>
public sealed class GitHubIntegration : Integration
{
    public const string TypeName = "github";

    /// <summary>GitHub org or user whose repos are synced.</summary>
    public string? Org { get; set; }

    /// <summary>Explicit repos as "owner/name", in addition to (or instead of) an org.</summary>
    public List<string> Repos { get; set; } = [];

    /// <summary>
    /// Historical floor for the first sync: commits dated before this, and pull requests with no
    /// activity since it, are not pulled — so a connection can start recent rather than replicating
    /// years of history. The incremental commit/PR cursors take over once they pass this.
    /// </summary>
    public DateTimeOffset? SyncSince { get; set; }
}

/// <summary>Jira Cloud connection: a site, an account email, and a single project. Pull-based.</summary>
public sealed class JiraIntegration : Integration
{
    public const string TypeName = "jira";

    /// <summary>Atlassian Cloud site URL, e.g. https://acme.atlassian.net.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Atlassian account email, paired with the API token to authenticate.</summary>
    public string? Email { get; set; }

    /// <summary>The single project key this connection syncs, e.g. "QAI".</summary>
    public string? ProjectKey { get; set; }

    /// <summary>
    /// When true, the API token is a scoped token: requests route through the Atlassian
    /// API gateway (<c>https://api.atlassian.com/ex/jira/{cloudId}</c>) rather than the
    /// site directly. Classic (unscoped) tokens use the site URL. Defaults to scoped.
    /// </summary>
    public bool ScopedToken { get; set; } = true;

    /// <summary>
    /// Historical floor for the first sync: issues updated before this are not pulled,
    /// so a connection can start a few sprints back rather than replicating everything.
    /// The incremental <c>issues:updated</c> cursor takes over once it passes this.
    /// </summary>
    public DateTimeOffset? SyncSince { get; set; }
}

/// <summary>
/// Tempo Timesheets connection: logged effort for a Jira project, pulled from the Tempo
/// Cloud API (api.tempo.io) with its own Bearer token (the base <see cref="Integration.EncryptedCredential"/>).
/// Standalone from Jira — different host and credential — but its worklog authors are Jira
/// account ids, so they resolve to the same Person.
/// </summary>
public sealed class TempoIntegration : Integration
{
    public const string TypeName = "tempo";

    /// <summary>
    /// The numeric Jira project id whose worklogs are synced, e.g. "10023". Tempo's v4 API scopes
    /// worklogs by numeric project id, not by the project key.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>Historical floor for the first sync: worklogs before this date are not pulled.</summary>
    public DateTimeOffset? SyncSince { get; set; }
}

/// <summary>
/// Claude Code usage connection. Push-based: Claude Code exports OTEL metrics to
/// Factarium's ingestion endpoint, so there is no polling configuration or credential.
/// </summary>
public sealed class ClaudeIntegration : Integration
{
    public const string TypeName = "claude-code";
}

public enum SyncRunStatus
{
    Never = 0,
    Running = 1,
    Success = 2,
    Failed = 3,
}
