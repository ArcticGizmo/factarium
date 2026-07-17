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

/// <summary>A tracker issue (Jira). Actor attribution is the assignee's identity.</summary>
public class CanonicalIssue
{
    public long Id { get; set; }
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public required string Key { get; set; }
    public string? ProjectKey { get; set; }
    public string? IssueType { get; set; }
    public string? Status { get; set; }
    public bool IsResolved { get; set; }
    public Guid? AssigneeIdentityId { get; set; }
    public string? AssigneeLogin { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
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
