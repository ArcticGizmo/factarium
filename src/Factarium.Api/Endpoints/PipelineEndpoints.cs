using Factarium.Application.Pipeline;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pipeline");

        group.MapGet("", async (FactariumDbContext db, CancellationToken ct) =>
        {
            var steps = await db.PipelineSteps.OrderBy(s => s.Name).ToListAsync(ct);
            return Results.Ok(steps.Select(s => new
            {
                s.Name,
                s.LastRunAt,
                s.LastStatus,
                s.LastItemsProcessed,
                s.LastError,
            }));
        });

        group.MapPost("run", async (IPipelineRunner runner, CancellationToken ct) =>
        {
            var result = await runner.RunAsync(force: true, ct);
            return Results.Ok(new
            {
                transformRan = result.TransformRan,
                aggregateRan = result.AggregateRan,
                transform = result.Transform,
                aggregate = result.Aggregate,
            });
        });

        return app;
    }
}
