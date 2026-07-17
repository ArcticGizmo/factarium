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
            var doneIssues = await db.CanonicalIssues.CountAsync(i => i.IsResolved, ct);
            var resolvedLast7d = await db.CanonicalIssues.CountAsync(i => i.ResolvedAt != null && i.ResolvedAt >= since, ct);

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

        return app;
    }
}
