using Factarium.Application.Aggregate;
using Factarium.Application.Pipeline;
using Factarium.Application.Transform;
using Factarium.Domain.Pipeline;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Factarium.Infrastructure.Pipeline;

internal sealed class PipelineRunner(
    FactariumDbContext db,
    ITransformService transform,
    IAggregateService aggregate,
    TimeProvider clock,
    ILogger<PipelineRunner> logger) : IPipelineRunner
{
    private const string TransformStep = "transform:github";
    private const string AggregateStep = "aggregate:daily";

    public async Task<PipelineRunResult> RunAsync(bool force, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var rawWatermark = await db.RawRecords
            .Where(r => r.Source == "github")
            .MaxAsync(r => (DateTimeOffset?)r.FetchedAt, cancellationToken);

        var transformState = await GetOrCreateAsync(TransformStep, cancellationToken);
        var transformShouldRun = force
            || (rawWatermark is not null
                && (transformState.LastInputWatermark is null || rawWatermark > transformState.LastInputWatermark));

        TransformResult? transformResult = null;
        if (transformShouldRun)
        {
            logger.LogInformation("Running transform (force={Force})", force);
            transformResult = await transform.TransformAsync(cancellationToken);
            transformState.LastInputWatermark = rawWatermark;
            Complete(transformState, now,
                transformResult.Repositories + transformResult.Commits
                + transformResult.PullRequests + transformResult.Reviews);
        }

        var aggregateState = await GetOrCreateAsync(AggregateStep, cancellationToken);
        var aggregateShouldRun = force || transformShouldRun || aggregateState.LastRunAt is null;

        AggregateResult? aggregateResult = null;
        if (aggregateShouldRun)
        {
            logger.LogInformation("Running aggregate");
            aggregateResult = await aggregate.AggregateAsync(cancellationToken);
            aggregateState.LastInputWatermark = rawWatermark;
            Complete(aggregateState, now, aggregateResult.MetricPoints);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PipelineRunResult(transformShouldRun, transformResult, aggregateShouldRun, aggregateResult);
    }

    private async Task<PipelineStep> GetOrCreateAsync(string name, CancellationToken cancellationToken)
    {
        var step = await db.PipelineSteps.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
        if (step is null)
        {
            step = new PipelineStep { Name = name };
            db.PipelineSteps.Add(step);
        }

        return step;
    }

    private static void Complete(PipelineStep step, DateTimeOffset now, int items)
    {
        step.LastRunAt = now;
        step.LastStatus = "success";
        step.LastError = null;
        step.LastItemsProcessed = items;
    }
}
