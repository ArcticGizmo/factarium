namespace Factarium.Application.Aggregate;

public sealed record AggregateResult(int MetricPoints);

/// <summary>
/// Rolls canonical entities (silver) up into daily materialized metrics (gold):
/// commit/PR counts and review latency, both overall and per actor. Actor identity
/// is resolved to a Person when mapped, else the identity stands in for itself, so
/// linking identities later collapses their series into the person's on the next run.
/// </summary>
public interface IAggregateService
{
    Task<AggregateResult> AggregateAsync(CancellationToken cancellationToken);
}
