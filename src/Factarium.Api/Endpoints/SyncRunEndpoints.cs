using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

/// <summary>
/// Read-only history of integration sync runs (scheduled and adhoc), powering the
/// Sync Activity view. Runs are appended by the sync service; nothing here mutates them.
/// </summary>
public static class SyncRunEndpoints
{
    public static IEndpointRouteBuilder MapSyncRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sync-runs");

        group.MapGet("", async (
            Guid? integrationId,
            string? status,
            string? entityType,
            int? page,
            int? pageSize,
            FactariumDbContext db,
            CancellationToken ct) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 200);
            var pageNumber = Math.Max(page ?? 1, 1);

            var query = db.SyncRuns.AsQueryable();

            if (integrationId is not null)
            {
                query = query.Where(r => r.IntegrationId == integrationId);
            }

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                query = query.Where(r => r.EntityType == entityType);
            }

            // Filter by run status when a recognised value is supplied (ignored otherwise).
            if (!string.IsNullOrWhiteSpace(status)
                && Enum.TryParse<SyncRunStatus>(status, ignoreCase: true, out var parsed))
            {
                query = query.Where(r => r.Status == parsed);
            }

            var total = await query.CountAsync(ct);

            var runs = await query
                .OrderByDescending(r => r.StartedAt)
                .ThenByDescending(r => r.Id)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .Select(r => new
                {
                    r.Id,
                    r.IntegrationId,
                    r.IntegrationName,
                    r.IntegrationType,
                    r.EntityType,
                    Trigger = r.Trigger.ToString(),
                    Status = r.Status.ToString(),
                    r.StartedAt,
                    r.CompletedAt,
                    r.RecordsWritten,
                    r.Error,
                })
                .ToListAsync(ct);

            return Results.Ok(new { Total = total, Page = pageNumber, PageSize = size, Runs = runs });
        });

        return app;
    }
}
