namespace Factarium.Domain.Canonical;

/// <summary>
/// Silver-tier entities: normalized, source-agnostic shapes produced from raw
/// records. Actor attribution is via <c>AuthorIdentityId</c>/<c>ReviewerIdentityId</c>
/// (a SourceIdentity); Person is resolved through that identity at aggregation time.
/// Each has a surrogate key plus a unique natural key including Source.
/// </summary>
public class CanonicalRepository
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public required string FullName { get; set; }
    public required string Name { get; set; }
    public required string Owner { get; set; }
    public string? DefaultBranch { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class CanonicalCommit
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string Sha { get; set; }
    public required string RepositoryFullName { get; set; }
    public Guid? AuthorIdentityId { get; set; }
    public string? AuthorLogin { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public string? Message { get; set; }
}

public class CanonicalPullRequest
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public int Number { get; set; }
    public required string RepositoryFullName { get; set; }
    public string? Title { get; set; }
    public required string State { get; set; }
    public bool IsMerged { get; set; }

    /// <summary>Target branch of the PR (e.g. "main"); used for the DORA deploy proxy.</summary>
    public string? BaseRef { get; set; }

    public Guid? AuthorIdentityId { get; set; }
    public string? AuthorLogin { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    // Diff stats from the PR detail endpoint (null when synced before these were captured).
    /// <summary>Lines added by the PR.</summary>
    public int? Additions { get; set; }

    /// <summary>Lines removed by the PR.</summary>
    public int? Deletions { get; set; }

    /// <summary>Files touched by the PR.</summary>
    public int? ChangedFiles { get; set; }

    /// <summary>Inline review comments left on the PR — a proxy for review depth.</summary>
    public int? ReviewCommentCount { get; set; }
}

/// <summary>
/// A single Claude Code usage measurement (from OTEL), normalized to a metric key
/// (cc_cost_usd, cc_tokens, cc_lines_added/removed, cc_sessions, …) and attributed
/// to a claude-code identity.
/// </summary>
public class CanonicalUsageMetric
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public required string MetricKey { get; set; }
    public double Value { get; set; }
    public Guid? ActorIdentityId { get; set; }
    public string? ActorLogin { get; set; }
    public string? SessionId { get; set; }
    public string? Model { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>
/// A tracker issue (Jira). The primary actor is the assignee's identity; the reporter
/// (createdBy), primary developer, and closer are captured as additional attributed actors.
/// Status is kept both workflow-specific (<see cref="Status"/>, e.g. "In Review") and by
/// category (<see cref="StatusCategory"/>, the "main type": To Do / In Progress / Done).
/// </summary>
public class CanonicalIssue
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public required string Key { get; set; }
    public string? ProjectKey { get; set; }
    public string? Title { get; set; }

    public string? IssueTypeId { get; set; }
    public string? IssueType { get; set; }

    /// <summary>Workflow-specific status name (e.g. "In Review").</summary>
    public string? Status { get; set; }

    /// <summary>Status category / "main type": To Do, In Progress, or Done.</summary>
    public string? StatusCategory { get; set; }

    public bool IsClosed { get; set; }
    public double? StoryPoints { get; set; }
    public int CommentCount { get; set; }

    public Guid? AssigneeIdentityId { get; set; }
    public string? AssigneeLogin { get; set; }

    /// <summary>The "reported by" account (createdBy in the UI's terms).</summary>
    public Guid? ReporterIdentityId { get; set; }
    public string? ReporterLogin { get; set; }

    /// <summary>Value of the "Primary Developer" custom field.</summary>
    public Guid? PrimaryDeveloperIdentityId { get; set; }
    public string? PrimaryDeveloperLogin { get; set; }

    /// <summary>Who closed the issue, derived from the changelog (resolution transition).</summary>
    public Guid? ClosedByIdentityId { get; set; }
    public string? ClosedByLogin { get; set; }

    /// <summary>The issue's current sprint (active, else the most recent it belongs to).</summary>
    public long? SprintId { get; set; }
    public string? SprintName { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Churn/rework counts, derived from the changelog by JiraChangelogTransformService.
    /// <summary>Times the issue re-entered a resolved state (resolution cleared then re-set).</summary>
    public int ReopenCount { get; set; }

    /// <summary>Number of assignee changes over the issue's life.</summary>
    public int ReassignmentCount { get; set; }

    /// <summary>Transitions to an earlier status category (e.g. In Review → In Progress).</summary>
    public int BackflowCount { get; set; }
}

/// <summary>
/// A contiguous interval an issue spent in one state, produced by replaying the changelog.
/// <see cref="Kind"/> is "status" (workflow status, attributed to the assignee at the time)
/// or "flagged" (blocked/impediment). Time-in-status-by-assignee and blocked time both come
/// from this table. Open intervals (<see cref="IsOpen"/>) run to the transform time.
/// </summary>
public class CanonicalIssueSegment
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string IssueKey { get; set; }
    public required string IssueExternalId { get; set; }
    public required string Kind { get; set; }

    /// <summary>Status name for Kind "status"; null for "flagged".</summary>
    public string? Value { get; set; }

    /// <summary>Status category (To Do / In Progress / Done) for Kind "status", when known.</summary>
    public string? Category { get; set; }

    /// <summary>The assignee at the time of the interval (unassigned = null).</summary>
    public Guid? AssigneeIdentityId { get; set; }
    public string? AssigneeLogin { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsOpen { get; set; }
}

/// <summary>
/// An interval during which an issue belonged to a sprint, replayed from the changelog's
/// Sprint field changes. Enables spillover / mid-sprint scope-creep analysis, which the
/// current-sprint snapshot on <see cref="CanonicalIssue"/> cannot express.
/// </summary>
public class CanonicalIssueSprintMembership
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string IssueKey { get; set; }
    public required string IssueExternalId { get; set; }
    public long SprintId { get; set; }
    public string? SprintName { get; set; }
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Null while the issue is still in the sprint.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
}

public class CanonicalReview
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public int PullRequestNumber { get; set; }
    public required string RepositoryFullName { get; set; }
    public Guid? ReviewerIdentityId { get; set; }
    public string? ReviewerLogin { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
}

/// <summary>
/// A logged-effort entry from Tempo. Actual time booked by a person against a Jira issue on a
/// given day. The author is a Jira account, so <see cref="AuthorIdentityId"/> resolves to the
/// same Person as the issues and PRs — giving effort alongside flow and delivery.
/// </summary>
public class CanonicalWorklog
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public string? IssueKey { get; set; }
    public string? IssueExternalId { get; set; }
    public Guid? AuthorIdentityId { get; set; }
    public string? AuthorLogin { get; set; }
    public int TimeSpentSeconds { get; set; }
    public int BillableSeconds { get; set; }
    public DateOnly? WorkDate { get; set; }
    public string? Description { get; set; }
}
