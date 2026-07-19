using Factarium.Application.Sync;
using Factarium.Domain.Sync;
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

    /// <summary>
    /// Data-map key stamped on manually-fired triggers by
    /// <see cref="QuartzIntegrationScheduler.TriggerNowAsync"/>. Cron triggers omit it,
    /// so its presence distinguishes an adhoc run from a scheduled one.
    /// </summary>
    public const string ManualTriggerKey = "manualTrigger";

    public async Task Execute(IJobExecutionContext context)
    {
        var raw = context.MergedJobDataMap.GetString(IntegrationIdKey);
        if (!Guid.TryParse(raw, out var integrationId))
        {
            logger.LogError("IntegrationSyncJob invoked without a valid '{Key}'", IntegrationIdKey);
            return;
        }

        var trigger = context.MergedJobDataMap.ContainsKey(ManualTriggerKey)
            ? SyncRunTrigger.Manual
            : SyncRunTrigger.Scheduled;

        await sync.RunAsync(integrationId, trigger, context.CancellationToken);
    }
}
