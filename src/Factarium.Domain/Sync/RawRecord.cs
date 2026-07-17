namespace Factarium.Domain.Sync;

/// <summary>
/// Bronze-tier replicated fact: a source record held as-is (JSON payload) with
/// provenance. Uniquely identified by (Source, EntityType, SourceId). Re-syncing
/// the same record upserts in place; <see cref="ContentHash"/> lets sync skip
/// unchanged rows and <see cref="Version"/> tracks how many times it changed.
/// </summary>
public class RawRecord
{
    public long Id { get; set; }

    public Guid IntegrationId { get; set; }

    public required string Source { get; set; }

    public required string EntityType { get; set; }

    public required string SourceId { get; set; }

    /// <summary>Raw JSON payload (stored as jsonb).</summary>
    public required string Payload { get; set; }

    /// <summary>SHA-256 of the payload, used for change detection on re-sync.</summary>
    public required string ContentHash { get; set; }

    /// <summary>Last-updated timestamp as reported by the source, when known.</summary>
    public DateTimeOffset? SourceUpdatedAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public int Version { get; set; }
}
