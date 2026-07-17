namespace Factarium.Domain.People;

/// <summary>
/// How a source represents a user (e.g. a GitHub login). Created automatically
/// during transform for every actor seen. Starts unmapped (<see cref="PersonId"/>
/// null); a user links it to a <see cref="Person"/> later. Aggregation attributes
/// activity to the linked Person, or treats the identity as its own actor while
/// unmapped — so mapping is never required to get data flowing.
/// </summary>
public class SourceIdentity
{
    public Guid Id { get; set; }

    public required string Source { get; set; }

    /// <summary>The source's handle for this user (GitHub login).</summary>
    public required string Login { get; set; }

    /// <summary>The source's stable id when known (GitHub numeric user id).</summary>
    public string? ExternalId { get; set; }

    public string? DisplayName { get; set; }

    public Guid? PersonId { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }
}
