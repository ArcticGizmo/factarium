using Factarium.Domain.Sync;
using Quartz;

namespace Factarium.Api.Scheduling;

/// <summary>Schedules, unschedules, and manually triggers integration sync jobs.</summary>
public interface IIntegrationScheduler
{
    Task ScheduleAsync(Integration integration, CancellationToken cancellationToken);
    Task UnscheduleAsync(Guid integrationId, CancellationToken cancellationToken);
    Task TriggerNowAsync(Guid integrationId, CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetNextRunAsync(Guid integrationId, CancellationToken cancellationToken);
}

internal sealed class QuartzIntegrationScheduler(ISchedulerFactory factory) : IIntegrationScheduler
{
    private const string Group = "integrations";

    public async Task ScheduleAsync(Integration integration, CancellationToken cancellationToken)
    {
        var scheduler = await factory.GetScheduler(cancellationToken);
        await EnsureJobAsync(scheduler, integration.Id, cancellationToken);

        var triggerKey = new TriggerKey(integration.Id.ToString(), Group);
        await scheduler.UnscheduleJob(triggerKey, cancellationToken);

        if (integration.Enabled && !string.IsNullOrWhiteSpace(integration.ScheduleCron))
        {
            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(KeyFor(integration.Id))
                .WithCronSchedule(integration.ScheduleCron)
                .Build();
            await scheduler.ScheduleJob(trigger, cancellationToken);
        }
    }

    public async Task TriggerNowAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        var scheduler = await factory.GetScheduler(cancellationToken);
        await EnsureJobAsync(scheduler, integrationId, cancellationToken);

        // Mark the fire as manual so the job records an adhoc (not scheduled) run.
        var data = new JobDataMap { { IntegrationSyncJob.ManualTriggerKey, "true" } };
        await scheduler.TriggerJob(KeyFor(integrationId), data, cancellationToken);
    }

    public async Task UnscheduleAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        var scheduler = await factory.GetScheduler(cancellationToken);
        await scheduler.DeleteJob(KeyFor(integrationId), cancellationToken);
    }

    public async Task<DateTimeOffset?> GetNextRunAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        var scheduler = await factory.GetScheduler(cancellationToken);
        var triggers = await scheduler.GetTriggersOfJob(KeyFor(integrationId), cancellationToken);

        DateTimeOffset? next = null;
        foreach (var trigger in triggers)
        {
            var fire = trigger.GetNextFireTimeUtc();
            if (fire is not null && (next is null || fire < next))
            {
                next = fire;
            }
        }

        return next;
    }

    private static async Task EnsureJobAsync(IScheduler scheduler, Guid integrationId, CancellationToken cancellationToken)
    {
        var jobKey = KeyFor(integrationId);
        if (await scheduler.CheckExists(jobKey, cancellationToken))
        {
            return;
        }

        var job = JobBuilder.Create<IntegrationSyncJob>()
            .WithIdentity(jobKey)
            .StoreDurably()
            .UsingJobData(IntegrationSyncJob.IntegrationIdKey, integrationId.ToString())
            .Build();

        await scheduler.AddJob(job, replace: true, cancellationToken);
    }

    private static JobKey KeyFor(Guid integrationId) => new(integrationId.ToString(), Group);
}
