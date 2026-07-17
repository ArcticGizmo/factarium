namespace Factarium.Domain.Metrics;

/// <summary>
/// Gold-tier materialized metric point: one value for a metric on a day, optionally
/// sliced by a dimension (e.g. repository) and an actor. Empty-string slots mean
/// "all" (all repos / all actors) so the natural key stays non-null and upsertable.
/// </summary>
public class DailyMetric
{
    public long Id { get; set; }

    public required string MetricKey { get; set; }

    public DateOnly Day { get; set; }

    /// <summary>Slice dimension, e.g. repository full name. "" = across all.</summary>
    public string Dimension { get; set; } = string.Empty;

    /// <summary>Actor slice: "person:{id}" or "identity:{id}". "" = across all actors.</summary>
    public string ActorKey { get; set; } = string.Empty;

    public string? ActorLabel { get; set; }

    public double Value { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
