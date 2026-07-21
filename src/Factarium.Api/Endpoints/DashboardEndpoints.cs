using Factarium.Application.Configuration;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Factarium.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards");

        // Windowed (default 30 days) so volume tiles can show a "vs previous period" delta,
        // matching the Delivery dashboard. Entity counts (repos/people/unmapped) are current
        // state, so they have no previous window.
        group.MapGet("repo-activity", async (FactariumDbContext db, TimeProvider clock, int? days, CancellationToken ct) =>
        {
            var windowDays = days is > 0 and <= 365 ? days.Value : 30;
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var curFrom = today.AddDays(-windowDays + 1);
            var prevTo = curFrom.AddDays(-1);
            var prevFrom = prevTo.AddDays(-windowDays + 1);

            async Task<List<object>> SeriesAsync(string metricKey)
            {
                var points = await db.DailyMetrics
                    .Where(m => m.MetricKey == metricKey && m.ActorKey == "" && m.Day >= curFrom && m.Day <= today)
                    .OrderBy(m => m.Day)
                    .Select(m => new { m.Day, m.Value })
                    .ToListAsync(ct);

                return points.Select(p => (object)new { day = p.Day.ToString("yyyy-MM-dd"), value = p.Value }).ToList();
            }

            async Task<double> SumAsync(string key, DateOnly from, DateOnly to) =>
                await db.DailyMetrics
                    .Where(m => m.MetricKey == key && m.ActorKey == "" && m.Day >= from && m.Day <= to)
                    .SumAsync(m => (double?)m.Value, ct) ?? 0;

            var commitsByActor = await db.DailyMetrics
                .Where(m => m.MetricKey == "commits" && m.ActorKey != "" && m.Day >= curFrom && m.Day <= today)
                .GroupBy(m => new { m.ActorKey, m.ActorLabel })
                .Select(g => new { g.Key.ActorLabel, Value = g.Sum(x => x.Value) })
                .OrderByDescending(x => x.Value)
                .Take(12)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                windowDays,
                totals = new
                {
                    commits = await SumAsync("commits", curFrom, today),
                    prsOpened = await SumAsync("prs_opened", curFrom, today),
                    prsMerged = await SumAsync("prs_merged", curFrom, today),
                    repositories = await db.CanonicalRepositories.CountAsync(ct),
                    people = await db.People.CountAsync(ct),
                    unmappedIdentities = await db.SourceIdentities.CountAsync(i => i.PersonId == null, ct),
                },
                previous = new
                {
                    commits = await SumAsync("commits", prevFrom, prevTo),
                    prsOpened = await SumAsync("prs_opened", prevFrom, prevTo),
                    prsMerged = await SumAsync("prs_merged", prevFrom, prevTo),
                },
                targets = await SettingsHelpers.LoadAsync<RepoActivityTargets>(db, RepoActivityTargets.SettingsKey, ct),
                commitsByDay = await SeriesAsync("commits"),
                prsOpenedByDay = await SeriesAsync("prs_opened"),
                prsMergedByDay = await SeriesAsync("prs_merged"),
                reviewLatencyByDay = await SeriesAsync("pr_first_review_hours"),
                commitsByActor = commitsByActor.Select(a => new { label = a.ActorLabel, value = a.Value }),
            });
        });

        group.MapPut("repo-activity/targets", async (FactariumDbContext db, TimeProvider clock, RepoActivityTargets targets, CancellationToken ct) =>
        {
            await SettingsHelpers.SaveAsync(db, clock, RepoActivityTargets.SettingsKey, targets, ct);
            return Results.Ok(targets);
        });

        // Delivery is windowed (default 30 days) so the tiles can show a "vs previous period"
        // delta: `totals` covers the current window, `previous` the window immediately before it.
        group.MapGet("delivery", async (FactariumDbContext db, IOptions<DoraOptions> dora, TimeProvider clock, int? days, CancellationToken ct) =>
        {
            var windowDays = days is > 0 and <= 365 ? days.Value : 30;
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var curFrom = today.AddDays(-windowDays + 1);
            var prevTo = curFrom.AddDays(-1);
            var prevFrom = prevTo.AddDays(-windowDays + 1);

            // Series drive the charts + tile sparklines: current window only, so they read
            // consistently with the tile totals above them.
            async Task<List<object>> SeriesAsync(string metricKey)
            {
                var points = await db.DailyMetrics
                    .Where(m => m.MetricKey == metricKey && m.ActorKey == "" && m.Day >= curFrom && m.Day <= today)
                    .OrderBy(m => m.Day)
                    .Select(m => new { m.Day, m.Value })
                    .ToListAsync(ct);
                return points.Select(p => (object)new { day = p.Day.ToString("yyyy-MM-dd"), value = p.Value }).ToList();
            }

            async Task<double> SumAsync(string key, DateOnly from, DateOnly to) =>
                await db.DailyMetrics
                    .Where(m => m.MetricKey == key && m.ActorKey == "" && m.Day >= from && m.Day <= to)
                    .SumAsync(m => (double?)m.Value, ct) ?? 0;

            async Task<double> AvgAsync(string key, DateOnly from, DateOnly to)
            {
                var values = await db.DailyMetrics
                    .Where(m => m.MetricKey == key && m.ActorKey == "" && m.Day >= from && m.Day <= to)
                    .Select(m => m.Value)
                    .ToListAsync(ct);
                return values.Count == 0 ? 0 : Math.Round(values.Average(), 1);
            }

            async Task<object> TotalsAsync(DateOnly from, DateOnly to) => new
            {
                deploys = await SumAsync("deploys", from, to),
                prsMerged = await SumAsync("prs_merged", from, to),
                issuesResolved = await SumAsync("issues_resolved", from, to),
                avgLeadTimeHours = await AvgAsync("pr_lead_time_hours", from, to),
                avgReviewLatencyHours = await AvgAsync("pr_first_review_hours", from, to),
                avgIssueCycleHours = await AvgAsync("issue_cycle_time_hours", from, to),
            };

            return Results.Ok(new
            {
                deployProxyBranch = dora.Value.DeployBranch,
                windowDays,
                totals = await TotalsAsync(curFrom, today),
                previous = await TotalsAsync(prevFrom, prevTo),
                targets = await SettingsHelpers.LoadAsync<DeliveryTargets>(db, DeliveryTargets.SettingsKey, ct),
                deploysByDay = await SeriesAsync("deploys"),
                prsMergedByDay = await SeriesAsync("prs_merged"),
                leadTimeByDay = await SeriesAsync("pr_lead_time_hours"),
                issuesResolvedByDay = await SeriesAsync("issues_resolved"),
                cycleTimeByDay = await SeriesAsync("issue_cycle_time_hours"),
            });
        });

        // Targets live with the dashboard they belong to, edited in-screen and shared via the
        // settings table. GET is folded into the delivery payload above; PUT persists edits.
        group.MapPut("delivery/targets", async (FactariumDbContext db, TimeProvider clock, DeliveryTargets targets, CancellationToken ct) =>
        {
            await SettingsHelpers.SaveAsync(db, clock, DeliveryTargets.SettingsKey, targets, ct);
            return Results.Ok(targets);
        });

        group.MapGet("claude-code", async (FactariumDbContext db, CancellationToken ct) =>
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

            double Sum(string key) => db.DailyMetrics.Where(m => m.MetricKey == key && m.ActorKey == "").Sum(m => m.Value);

            var costByActor = await db.DailyMetrics
                .Where(m => m.MetricKey == "cc_cost_usd" && m.ActorKey != "")
                .GroupBy(m => new { m.ActorKey, m.ActorLabel })
                .Select(g => new { g.Key.ActorLabel, Value = g.Sum(x => x.Value) })
                .OrderByDescending(x => x.Value)
                .Take(12)
                .ToListAsync(ct);

            return Results.Ok(new
            {
                totals = new
                {
                    costUsd = Math.Round(Sum("cc_cost_usd"), 2),
                    tokens = Sum("cc_tokens"),
                    linesAdded = Sum("cc_lines_added"),
                    linesRemoved = Sum("cc_lines_removed"),
                    sessions = Sum("cc_sessions"),
                },
                costByDay = await SeriesAsync("cc_cost_usd"),
                tokensByDay = await SeriesAsync("cc_tokens"),
                linesAddedByDay = await SeriesAsync("cc_lines_added"),
                sessionsByDay = await SeriesAsync("cc_sessions"),
                costByActor = costByActor.Select(a => new { label = a.ActorLabel, value = Math.Round(a.Value, 2) }),
            });
        });

        return app;
    }
}
