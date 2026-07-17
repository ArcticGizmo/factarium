using Factarium.Application.Aggregate;
using Factarium.Application.Transform;

namespace Factarium.Application.Pipeline;

public sealed record PipelineRunResult(
    bool TransformRan,
    TransformResult? Transform,
    bool AggregateRan,
    AggregateResult? Aggregate);

/// <summary>
/// Runs the transform → aggregate loop with staleness gating: transform runs only
/// when new raw data has arrived; aggregate runs when the transform ran (or when
/// forced, e.g. after an identity is remapped). <paramref name="force"/> bypasses
/// the gates.
/// </summary>
public interface IPipelineRunner
{
    Task<PipelineRunResult> RunAsync(bool force, CancellationToken cancellationToken);
}
