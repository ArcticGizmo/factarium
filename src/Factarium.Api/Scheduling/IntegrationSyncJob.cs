using Factarium.Application.Sync;
using Quartz;

namespace Factarium.Api.Scheduling;

/// <summary>
/// Quartz job that runs one integration's sync. Concurrent execution of the same
/// job (same integration) is disallowed so scheduled and manual triggers can't
/// overlap. A DI scope is created per execution by the Quartz DI job factory.
/// </summary>
[DisallowConcurrentExecution]
public sealed class IntegrationSyncJob(IIntegrationSyncService sync, ILogger<IntegrationSyncJob> logger) : IJob
{
    public const string IntegrationIdKey = "integrationId";

    public async Task Execute(IJobExecutionContext context)
    {
        var raw = context.MergedJobDataMap.GetString(IntegrationIdKey);
        if (!Guid.TryParse(raw, out var integrationId))
        {
            logger.LogError("IntegrationSyncJob invoked without a valid '{Key}'", IntegrationIdKey);
            return;
        }

        await sync.RunAsync(integrationId, context.CancellationToken);
    }
}
