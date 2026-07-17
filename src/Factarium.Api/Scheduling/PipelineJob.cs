using Factarium.Application.Pipeline;
using Quartz;

namespace Factarium.Api.Scheduling;

/// <summary>
/// Scheduled transform + aggregate run. Runs staleness-gated (force=false) so it
/// is cheap when no new data has arrived; the DB pipeline_steps rows decide whether
/// work is actually done.
/// </summary>
[DisallowConcurrentExecution]
public sealed class PipelineJob(IPipelineRunner runner, ILogger<PipelineJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var result = await runner.RunAsync(force: false, context.CancellationToken);
        if (result.TransformRan || result.AggregateRan)
        {
            logger.LogInformation(
                "Pipeline ran (transform={Transform}, aggregate={Aggregate})",
                result.TransformRan, result.AggregateRan);
        }
    }
}
