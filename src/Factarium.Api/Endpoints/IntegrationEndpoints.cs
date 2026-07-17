using System.Text.Json;
using Factarium.Api.Scheduling;
using Factarium.Application.Security;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

public sealed record CreateIntegrationRequest(
    string Type,
    string Name,
    string? Cron,
    bool Enabled,
    Dictionary<string, string?>? Settings,
    string? Credential);

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations");

        group.MapGet("", async (FactariumDbContext db, IIntegrationScheduler scheduler, CancellationToken ct) =>
        {
            var integrations = await db.Integrations.OrderBy(i => i.Name).ToListAsync(ct);

            var result = new List<object>(integrations.Count);
            foreach (var i in integrations)
            {
                result.Add(new
                {
                    i.Id,
                    i.Type,
                    i.Name,
                    i.Enabled,
                    i.ScheduleCron,
                    LastRunStatus = i.LastRunStatus.ToString(),
                    i.LastRunStartedAt,
                    i.LastRunCompletedAt,
                    i.LastRunError,
                    i.LastRunRecordsWritten,
                    NextRunAt = await scheduler.GetNextRunAsync(i.Id, ct),
                    HasCredential = i.EncryptedCredential is not null,
                });
            }

            return Results.Ok(result);
        });

        group.MapPost("", async (
            CreateIntegrationRequest request,
            FactariumDbContext db,
            ICredentialProtector protector,
            IIntegrationScheduler scheduler,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Type and Name are required.");
            }

            var integration = new Integration
            {
                Id = Guid.NewGuid(),
                Type = request.Type,
                Name = request.Name,
                Enabled = request.Enabled,
                ScheduleCron = request.Cron,
                SettingsJson = request.Settings is null ? null : JsonSerializer.Serialize(request.Settings),
                EncryptedCredential = string.IsNullOrWhiteSpace(request.Credential)
                    ? null
                    : protector.Protect(request.Credential),
                CreatedAt = clock.GetUtcNow(),
            };

            db.Integrations.Add(integration);
            await db.SaveChangesAsync(ct);

            if (integration is { Enabled: true, ScheduleCron: not null })
            {
                await scheduler.ScheduleAsync(integration, ct);
            }

            return Results.Created($"/api/integrations/{integration.Id}", new { integration.Id });
        });

        group.MapPost("{id:guid}/sync", async (
            Guid id, FactariumDbContext db, IIntegrationScheduler scheduler, CancellationToken ct) =>
        {
            if (!await db.Integrations.AnyAsync(i => i.Id == id, ct))
            {
                return Results.NotFound();
            }

            await scheduler.TriggerNowAsync(id, ct);
            return Results.Accepted($"/api/integrations/{id}");
        });

        group.MapDelete("{id:guid}", async (
            Guid id, FactariumDbContext db, IIntegrationScheduler scheduler, CancellationToken ct) =>
        {
            var integration = await db.Integrations.FindAsync([id], ct);
            if (integration is null)
            {
                return Results.NotFound();
            }

            await scheduler.UnscheduleAsync(id, ct);
            db.Integrations.Remove(integration);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }
}
