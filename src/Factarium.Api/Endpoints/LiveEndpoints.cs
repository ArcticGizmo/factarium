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
            var since = clock.GetUtcNow().AddDays(-7);

            var openPrs = await db.CanonicalPullRequests.CountAsync(p => p.State == "open", ct);
            var mergedLast7d = await db.CanonicalPullRequests.CountAsync(p => p.MergedAt != null && p.MergedAt >= since, ct);
            var commitsLast7d = await db.CanonicalCommits.CountAsync(c => c.CommittedAt != null && c.CommittedAt >= since, ct);

            var issuesByStatus = await db.CanonicalIssues
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key ?? "(none)", Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToListAsync(ct);

            var totalIssues = issuesByStatus.Sum(x => x.Count);
            var doneIssues = await db.CanonicalIssues.CountAsync(i => i.IsClosed, ct);
            var resolvedLast7d = await db.CanonicalIssues.CountAsync(i => i.ClosedAt != null && i.ClosedAt >= since, ct);

            return Results.Ok(new
            {
                generatedAt = clock.GetUtcNow(),
                pullRequests = new
                {
                    open = openPrs,
                    mergedLast7d,
                },
                commits = new { last7d = commitsLast7d },
                issues = new
                {
                    total = totalIssues,
                    done = doneIssues,
                    resolvedLast7d,
                    completionPct = totalIssues == 0 ? 0 : Math.Round(100.0 * doneIssues / totalIssues, 1),
                    byStatus = issuesByStatus.Select(x => new { status = x.Status, count = x.Count }),
                },
            });
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
            });
        });

        return app;
    }
}
