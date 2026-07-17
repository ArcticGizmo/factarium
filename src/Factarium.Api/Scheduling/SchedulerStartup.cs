using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Scheduling;

/// <summary>
/// On startup, (re)schedules Quartz cron triggers for every enabled integration
/// that has a schedule. The database is the source of truth; the in-memory Quartz
/// store is rebuilt from it on each boot.
/// </summary>
public sealed class SchedulerStartup(
    IServiceProvider services,
    IIntegrationScheduler scheduler,
    ILogger<SchedulerStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FactariumDbContext>();

        var scheduled = await db.Integrations
            .Where(i => i.Enabled && i.ScheduleCron != null)
            .ToListAsync(cancellationToken);

        foreach (var integration in scheduled)
        {
            await scheduler.ScheduleAsync(integration, cancellationToken);
        }

        logger.LogInformation("Scheduled {Count} integration(s) on startup", scheduled.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
