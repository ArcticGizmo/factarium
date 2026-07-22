using Factarium.Application.Configuration;
using Factarium.Domain.Canonical;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Factarium.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboards");

        // Windowed (default 30 days) code-review & repo-health view. Unlike Delivery (which counts
        // throughput), this reads the canonical PR/review/commit tables directly to derive review
        // coverage, PR size, flow, and a per-repo breakdown. Health ratios show a "vs previous
        // period" delta; current-state figures (open PRs, unmapped identities) have no previous.
        group.MapGet("repo-activity", async (FactariumDbContext db, TimeProvider clock, int? days, CancellationToken ct) =>
        {
            var windowDays = days is > 0 and <= 365 ? days.Value : 30;
            var now = clock.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var curFrom = today.AddDays(-windowDays + 1);

            static DateTimeOffset Midnight(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var winStart = Midnight(curFrom);
            var winEnd = Midnight(today.AddDays(1)); // exclusive: through end of today
            var prevStart = Midnight(curFrom.AddDays(-windowDays));
            var prevEnd = winStart;

            var prs = await db.CanonicalPullRequests.AsNoTracking().ToListAsync(ct);
            var reviews = await db.CanonicalReviews.AsNoTracking().ToListAsync(ct);
            var commits = await db.CanonicalCommits.AsNoTracking()
                .Where(c => c.CommittedAt != null).ToListAsync(ct);

            // Reviews grouped by their PR, so each PR can be asked what reviews it received.
            var reviewsByPr = reviews
                .GroupBy(r => (r.RepositoryFullName, r.PullRequestNumber))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Identity → display label (Person name when mapped, else the login), for the
            // reviewer-distribution chart.
            var identities = await db.SourceIdentities.AsNoTracking()
                .Select(i => new { i.Id, i.PersonId, i.Login }).ToListAsync(ct);
            var people = await db.People.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
            var labelById = identities.ToDictionary(
                i => i.Id,
                i => i.PersonId is { } pid && people.TryGetValue(pid, out var name) ? name : i.Login);
            string ReviewerLabel(CanonicalReview r) =>
                r.ReviewerIdentityId is { } id && labelById.TryGetValue(id, out var l) ? l : (r.ReviewerLogin ?? "unknown");

            static int Lines(CanonicalPullRequest p) => (p.Additions ?? 0) + (p.Deletions ?? 0);
            static bool HasSize(CanonicalPullRequest p) => p.Additions != null || p.Deletions != null;

            static double Median(IReadOnlyList<int> values)
            {
                if (values.Count == 0) return 0;
                var sorted = values.OrderBy(v => v).ToList();
                var mid = sorted.Count / 2;
                return sorted.Count % 2 == 1 ? sorted[mid] : Math.Round((sorted[mid - 1] + sorted[mid]) / 2.0, 1);
            }

            // First review submitted on a PR (null if never reviewed).
            DateTimeOffset? FirstReview(CanonicalPullRequest p)
            {
                if (!reviewsByPr.TryGetValue((p.RepositoryFullName, p.Number), out var rs)) return null;
                var times = rs.Where(r => r.SubmittedAt != null).Select(r => r.SubmittedAt!.Value).ToList();
                return times.Count == 0 ? null : times.Min();
            }

            // A PR is "covered" if it received a review at or before it merged.
            bool CoveredBeforeMerge(CanonicalPullRequest p)
            {
                var first = FirstReview(p);
                return first != null && (p.MergedAt == null || first <= p.MergedAt);
            }

            List<CanonicalPullRequest> MergedIn(DateTimeOffset from, DateTimeOffset to) =>
                prs.Where(p => p.MergedAt >= from && p.MergedAt < to).ToList();

            // The health ratios, computed for an arbitrary window so we can produce a "previous"
            // bundle for the tile deltas.
            (double Coverage, double MedianLines, double Depth, double Abandon) Bundle(DateTimeOffset from, DateTimeOffset to)
            {
                var merged = MergedIn(from, to);
                var coverage = merged.Count == 0 ? 0
                    : Math.Round(100.0 * merged.Count(CoveredBeforeMerge) / merged.Count, 1);
                var median = Median(merged.Where(HasSize).Select(Lines).ToList());
                var depthVals = merged.Where(p => p.ReviewCommentCount != null)
                    .Select(p => (double)p.ReviewCommentCount!.Value).ToList();
                var depth = depthVals.Count == 0 ? 0 : Math.Round(depthVals.Average(), 1);
                var closed = prs.Where(p => p.ClosedAt >= from && p.ClosedAt < to).ToList();
                var abandon = closed.Count == 0 ? 0 : Math.Round(100.0 * closed.Count(p => !p.IsMerged) / closed.Count, 1);
                return (coverage, median, depth, abandon);
            }

            var cur = Bundle(winStart, winEnd);
            var prev = Bundle(prevStart, prevEnd);
            var mergedInWin = MergedIn(winStart, winEnd);

            // Flow / WIP — current state, not windowed.
            var openPrs = prs.Where(p => p.State == "open").ToList();
            var openCreated = openPrs.Where(p => p.CreatedAt != null).Select(p => p.CreatedAt!.Value).ToList();
            var oldestOpenPrDays = openCreated.Count == 0 ? 0 : Math.Round((now - openCreated.Min()).TotalDays, 1);

            // Coverage trend: per merge-day share of merged PRs that were reviewed before merge.
            var coverageByDay = mergedInWin
                .GroupBy(p => DateOnly.FromDateTime(p.MergedAt!.Value.UtcDateTime))
                .OrderBy(g => g.Key)
                .Select(g => (object)new
                {
                    day = g.Key.ToString("yyyy-MM-dd"),
                    value = Math.Round(100.0 * g.Count(CoveredBeforeMerge) / g.Count(), 1),
                })
                .ToList();

            // PR-size distribution over merged PRs with known size, in fixed bucket order.
            string SizeBucket(int lines) => lines < 10 ? "XS (<10)"
                : lines < 100 ? "S (10–99)"
                : lines < 500 ? "M (100–499)"
                : lines < 1000 ? "L (500–999)"
                : "XL (1000+)";
            string[] bucketOrder = ["XS (<10)", "S (10–99)", "M (100–499)", "L (500–999)", "XL (1000+)"];
            var sizeCounts = mergedInWin.Where(HasSize)
                .GroupBy(p => SizeBucket(Lines(p)))
                .ToDictionary(g => g.Key, g => g.Count());
            var prSizeDistribution = bucketOrder
                .Select(b => (object)new { label = b, value = (double)(sizeCounts.TryGetValue(b, out var c) ? c : 0) })
                .ToList();

            // Reviewer distribution: reviews submitted in the window, by reviewer (bus-factor read).
            var reviewsByReviewer = reviews
                .Where(r => r.SubmittedAt >= winStart && r.SubmittedAt < winEnd)
                .GroupBy(ReviewerLabel)
                .Select(g => new { label = g.Key, value = (double)g.Count() })
                .OrderByDescending(x => x.value)
                .Take(12)
                .ToList();

            // Per-repo breakdown — the anchor of the page. Union of repos that saw a commit or a
            // merge in the window, so quiet repos drop off rather than padding the table.
            var repoNames = mergedInWin.Select(p => p.RepositoryFullName)
                .Concat(commits.Where(c => c.CommittedAt >= winStart && c.CommittedAt < winEnd)
                    .Select(c => c.RepositoryFullName))
                .Distinct();
            var repoTable = repoNames.Select(repo =>
            {
                var repoMerged = mergedInWin.Where(p => p.RepositoryFullName == repo).ToList();
                var repoCommits = commits.Count(c => c.RepositoryFullName == repo
                    && c.CommittedAt >= winStart && c.CommittedAt < winEnd);
                var latencies = repoMerged
                    .Select(p => (p, first: FirstReview(p)))
                    .Where(x => x.first != null && x.p.CreatedAt != null && x.first >= x.p.CreatedAt)
                    .Select(x => (x.first!.Value - x.p.CreatedAt!.Value).TotalHours)
                    .ToList();
                return new
                {
                    repo,
                    commits = repoCommits,
                    prsMerged = repoMerged.Count,
                    reviewCoveragePct = repoMerged.Count == 0 ? 0
                        : Math.Round(100.0 * repoMerged.Count(CoveredBeforeMerge) / repoMerged.Count, 1),
                    medianPrLines = Median(repoMerged.Where(HasSize).Select(Lines).ToList()),
                    avgReviewLatencyHours = latencies.Count == 0 ? (double?)null : Math.Round(latencies.Average(), 1),
                };
            })
            .OrderByDescending(r => r.prsMerged).ThenByDescending(r => r.commits)
            .ToList();

            // Review-latency trend stays sourced from the aggregated daily metric.
            var latencyByDay = await db.DailyMetrics
                .Where(m => m.MetricKey == "pr_first_review_hours" && m.ActorKey == "" && m.Day >= curFrom && m.Day <= today)
                .OrderBy(m => m.Day)
                .Select(m => new { m.Day, m.Value })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                windowDays,
                totals = new
                {
                    reviewCoveragePct = cur.Coverage,
                    medianPrLines = cur.MedianLines,
                    reviewDepth = cur.Depth,
                    abandonRatePct = cur.Abandon,
                    oldestOpenPrDays,
                    openPrs = openPrs.Count,
                    prsMerged = mergedInWin.Count,
                    repositories = await db.CanonicalRepositories.CountAsync(ct),
                    unmappedIdentities = await db.SourceIdentities.CountAsync(i => i.PersonId == null, ct),
                },
                previous = new
                {
                    reviewCoveragePct = prev.Coverage,
                    medianPrLines = prev.MedianLines,
                    reviewDepth = prev.Depth,
                    abandonRatePct = prev.Abandon,
                },
                targets = await SettingsHelpers.LoadAsync<RepoActivityTargets>(db, RepoActivityTargets.SettingsKey, ct),
                reviewLatencyByDay = latencyByDay.Select(p => new { day = p.Day.ToString("yyyy-MM-dd"), value = p.Value }),
                coverageByDay,
                prSizeDistribution,
                reviewsByReviewer,
                repoTable,
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
