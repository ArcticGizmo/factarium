namespace Factarium.Domain.Pipeline;

/// <summary>
/// Persisted state for a pipeline step (a transform or an aggregation). Drives
/// staleness gating: a step runs only when its input has advanced past
/// <see cref="LastInputWatermark"/>, so cycles aren't spent recomputing unchanged data.
/// </summary>
public class PipelineStep
{
    public required string Name { get; set; }

    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Newest input timestamp processed by the last successful run.</summary>
    public DateTimeOffset? LastInputWatermark { get; set; }

    public string LastStatus { get; set; } = "never";

    public string? LastError { get; set; }

    public int LastItemsProcessed { get; set; }
}
