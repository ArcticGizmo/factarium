using Factarium.Application.Configuration;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

/// <summary>
/// Live/transient metrics: current-state answers computed directly from canonical
/// tables on each request (no gold rollup), so they always reflect the latest sync.
/// </summary>
public static class LiveEndpoints
{
    public static IEndpointRouteBuilder MapLiveEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/live/summary", async (FactariumDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var since = now.AddDays(-7);
            var prevSince = now.AddDays(-14); // the 7 days before the current 7, for the delta.

            var openPrs = await db.CanonicalPullRequests.CountAsync(p => p.State == "open", ct);
            var mergedLast7d = await db.CanonicalPullRequests.CountAsync(p => p.MergedAt != null && p.MergedAt >= since, ct);
            var mergedPrev7d = await db.CanonicalPullRequests.CountAsync(p => p.MergedAt != null && p.MergedAt >= prevSince && p.MergedAt < since, ct);
            var commitsLast7d = await db.CanonicalCommits.CountAsync(c => c.CommittedAt != null && c.CommittedAt >= since, ct);
            var commitsPrev7d = await db.CanonicalCommits.CountAsync(c => c.CommittedAt != null && c.CommittedAt >= prevSince && c.CommittedAt < since, ct);

            var issuesByStatus = await db.CanonicalIssues
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key ?? "(none)", Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(ct);

            var totalIssues = issuesByStatus.Sum(x => x.Count);
            var doneIssues = await db.CanonicalIssues.CountAsync(i => i.IsClosed, ct);
            var resolvedLast7d = await db.CanonicalIssues.CountAsync(i => i.ClosedAt != null && i.ClosedAt >= since, ct);
            var resolvedPrev7d = await db.CanonicalIssues.CountAsync(i => i.ClosedAt != null && i.ClosedAt >= prevSince && i.ClosedAt < since, ct);

            // Tile sparklines: last 14 days of the gold daily rollup (empty until the pipeline runs).
            var sparkFrom = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-13);
            var sparkTo = DateOnly.FromDateTime(now.UtcDateTime);
            async Task<List<double>> SparkAsync(string key) =>
                await db.DailyMetrics
                    .Where(m => m.MetricKey == key && m.ActorKey == "" && m.Day >= sparkFrom && m.Day <= sparkTo)
                    .OrderBy(m => m.Day)
                    .Select(m => m.Value)
                    .ToListAsync(ct);

            return Results.Ok(new
            {
                generatedAt = now,
                pullRequests = new
                {
                    open = openPrs,
                    mergedLast7d,
                    mergedPrev7d,
                },
                commits = new { last7d = commitsLast7d, prev7d = commitsPrev7d },
                issues = new
                {
                    total = totalIssues,
                    done = doneIssues,
                    resolvedLast7d,
                    resolvedPrev7d,
                    completionPct = totalIssues == 0 ? 0 : Math.Round(100.0 * doneIssues / totalIssues, 1),
                    byStatus = issuesByStatus.Select(x => new { status = x.Status, count = x.Count }),
                },
                targets = await SettingsHelpers.LoadAsync<OverviewTargets>(db, OverviewTargets.SettingsKey, ct),
                spark = new
                {
                    commits = await SparkAsync("commits"),
                    merged = await SparkAsync("prs_merged"),
                    resolved = await SparkAsync("issues_resolved"),
                },
            });
        });

        app.MapPut("/api/live/summary/targets", async (FactariumDbContext db, TimeProvider clock, OverviewTargets targets, CancellationToken ct) =>
        {
            await SettingsHelpers.SaveAsync(db, clock, OverviewTargets.SettingsKey, targets, ct);
            return Results.Ok(targets);
        });

        // Changelog-derived flow: cumulative time-in-status by assignee, blocked time, and
        // churn — computed straight from the canonical segment table (the faithful answer).
        app.MapGet("/api/live/issue-flow", async (FactariumDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var identities = await db.SourceIdentities.AsNoTracking().ToListAsync(ct);
            var people = await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
            var labels = identities.ToDictionary(
                i => i.Id,
                i => i.PersonId is { } pid && people.TryGetValue(pid, out var name) ? name : i.DisplayName ?? i.Login);

            string Label(Guid? id) => id is { } gid && labels.TryGetValue(gid, out var name) ? name : "unassigned";
            static double Hours(IEnumerable<double> seconds) => Math.Round(seconds.Sum() / 3600.0, 2);

            var segments = await db.CanonicalIssueSegments.AsNoTracking().ToListAsync(ct);

            var timeInStatus = segments
                .Where(s => s.Kind == "status")
                .GroupBy(s => new { Assignee = Label(s.AssigneeIdentityId), s.Value, s.Category })
                .Select(g => new
                {
                    assignee = g.Key.Assignee,
                    status = g.Key.Value,
                    category = g.Key.Category,
                    hours = Hours(g.Select(x => x.DurationSeconds)),
                })
                .OrderByDescending(x => x.hours)
                .ToList();

            var blocked = segments
                .Where(s => s.Kind == "flagged")
                .GroupBy(s => Label(s.AssigneeIdentityId))
                .Select(g => new { assignee = g.Key, hours = Hours(g.Select(x => x.DurationSeconds)) })
                .OrderByDescending(x => x.hours)
                .ToList();

            var issues = await db.CanonicalIssues.AsNoTracking().Where(i => i.Source == "jira").ToListAsync(ct);

            return Results.Ok(new
            {
                generatedAt = clock.GetUtcNow(),
                timeInStatus,
                blocked,
                churn = new
                {
                    reopens = issues.Sum(i => i.ReopenCount),
                    reassignments = issues.Sum(i => i.ReassignmentCount),
                    backflow = issues.Sum(i => i.BackflowCount),
                },
                targets = await SettingsHelpers.LoadAsync<FlowTargets>(db, FlowTargets.SettingsKey, ct),
            });
        });

        app.MapPut("/api/live/issue-flow/targets", async (FactariumDbContext db, TimeProvider clock, FlowTargets targets, CancellationToken ct) =>
        {
            await SettingsHelpers.SaveAsync(db, clock, FlowTargets.SettingsKey, targets, ct);
            return Results.Ok(targets);
        });

        return app;
    }
}
