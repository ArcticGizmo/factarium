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
    public Guid? AuthorIdentityId { get; set; }
    public string? AuthorLogin { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
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
