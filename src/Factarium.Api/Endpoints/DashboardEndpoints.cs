using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards");

        group.MapGet("repo-activity", async (FactariumDbContext db, CancellationToken ct) =>
        {
            async Task<List<object>> SeriesAsync(string metricKey)
            {
                var points = await db.DailyMetrics
                    .Where(m => m.MetricKey == metricKey && m.ActorKey == "")
                    .OrderBy(m => m.Day)
                    .Select(m => new { m.Day, m.Value })
                    .ToListAsync(ct);

                return points.Select(p => (object)new { day = p.Day.ToString("yyyy-MM-dd"), value = p.Value }).ToList();
            }

            var commitsByActor = await db.DailyMetrics
                .Where(m => m.MetricKey == "commits" && m.ActorKey != "")
                .GroupBy(m => new { m.ActorKey, m.ActorLabel })
                .Select(g => new { g.Key.ActorLabel, Value = g.Sum(x => x.Value) })
                .OrderByDescending(x => x.Value)
                .Take(12)
                .ToListAsync(ct);

            double Total(string key) => db.DailyMetrics
                .Where(m => m.MetricKey == key && m.ActorKey == "")
                .Sum(m => m.Value);

            return Results.Ok(new
            {
                totals = new
                {
                    commits = Total("commits"),
                    prsOpened = Total("prs_opened"),
                    prsMerged = Total("prs_merged"),
                    repositories = await db.CanonicalRepositories.CountAsync(ct),
                    people = await db.People.CountAsync(ct),
                    unmappedIdentities = await db.SourceIdentities.CountAsync(i => i.PersonId == null, ct),
                },
                commitsByDay = await SeriesAsync("commits"),
                prsOpenedByDay = await SeriesAsync("prs_opened"),
                prsMergedByDay = await SeriesAsync("prs_merged"),
                reviewLatencyByDay = await SeriesAsync("pr_first_review_hours"),
                commitsByActor = commitsByActor.Select(a => new { label = a.ActorLabel, value = a.Value }),
            });
        });

        return app;
    }
}
