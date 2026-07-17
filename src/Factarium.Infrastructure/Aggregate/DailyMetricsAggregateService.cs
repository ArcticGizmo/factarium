using Factarium.Application.Aggregate;
using Factarium.Application.Configuration;
using Factarium.Domain.Metrics;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Factarium.Infrastructure.Aggregate;

/// <summary>
/// Full-recompute aggregation: clears and rebuilds <c>daily_metrics</c> from the
/// canonical tables. Produces repo/PR activity counts, per-actor rollups, and the
/// DORA-ish delivery metrics (deploy proxy = merges to the configured branch, PR
/// lead time, issue throughput and cycle time).
/// </summary>
internal sealed class DailyMetricsAggregateService(
    FactariumDbContext db,
    TimeProvider clock,
    IOptions<DoraOptions> dora) : IAggregateService
{
    private const string AllActors = "";
    private const string AllRepos = "";

    public async Task<AggregateResult> AggregateAsync(CancellationToken cancellationToken)
    {
        var deployBranch = dora.Value.DeployBranch;
        var actors = await BuildActorLookupAsync(cancellationToken);
        var now = clock.GetUtcNow();

        var counts = new Dictionary<(string Metric, DateOnly Day, string ActorKey), (double Value, string? Label)>();
        var averages = new Dictionary<string, Dictionary<DateOnly, (double Sum, int Count)>>();

        void AddCount(string metric, DateTimeOffset? when, Guid? identityId)
        {
            if (when is null)
            {
                return;
            }

            var day = DateOnly.FromDateTime(when.Value.UtcDateTime);
            var (actorKey, label) = actors.Resolve(identityId);
            Bump(counts, (metric, day, AllActors), 1, null);
            Bump(counts, (metric, day, actorKey), 1, label);
        }

        void AddAverage(string metric, DateTimeOffset? day, double value)
        {
            if (day is null || value < 0)
            {
                return;
            }

            var bucket = averages.TryGetValue(metric, out var existing)
                ? existing
                : averages[metric] = new Dictionary<DateOnly, (double, int)>();
            var d = DateOnly.FromDateTime(day.Value.UtcDateTime);
            bucket.TryGetValue(d, out var acc);
            bucket[d] = (acc.Sum + value, acc.Count + 1);
        }

        foreach (var commit in await db.CanonicalCommits.AsNoTracking().ToListAsync(cancellationToken))
        {
            AddCount("commits", commit.CommittedAt, commit.AuthorIdentityId);
        }

        foreach (var pr in await db.CanonicalPullRequests.AsNoTracking().ToListAsync(cancellationToken))
        {
            AddCount("prs_opened", pr.CreatedAt, pr.AuthorIdentityId);
            if (pr.IsMerged)
            {
                AddCount("prs_merged", pr.MergedAt, pr.AuthorIdentityId);

                // DORA deploy proxy: a merge to the configured branch is a "deployment".
                if (string.Equals(pr.BaseRef, deployBranch, StringComparison.OrdinalIgnoreCase))
                {
                    AddCount("deploys", pr.MergedAt, pr.AuthorIdentityId);
                }

                if (pr.CreatedAt is not null && pr.MergedAt is not null)
                {
                    AddAverage("pr_lead_time_hours", pr.MergedAt, (pr.MergedAt.Value - pr.CreatedAt.Value).TotalHours);
                }
            }
        }

        foreach (var issue in await db.CanonicalIssues.AsNoTracking().ToListAsync(cancellationToken))
        {
            if (issue is { IsResolved: true, ResolvedAt: not null })
            {
                AddCount("issues_resolved", issue.ResolvedAt, issue.AssigneeIdentityId);
                if (issue.CreatedAt is not null)
                {
                    AddAverage("issue_cycle_time_hours", issue.ResolvedAt, (issue.ResolvedAt.Value - issue.CreatedAt.Value).TotalHours);
                }
            }
        }

        var metrics = counts.Select(kvp => new DailyMetric
        {
            MetricKey = kvp.Key.Metric,
            Day = kvp.Key.Day,
            Dimension = AllRepos,
            ActorKey = kvp.Key.ActorKey,
            ActorLabel = kvp.Value.Label,
            Value = kvp.Value.Value,
            ComputedAt = now,
        }).ToList();

        metrics.AddRange(await BuildReviewLatencyAsync(now, cancellationToken));
        metrics.AddRange(averages.SelectMany(m => m.Value.Select(day => new DailyMetric
        {
            MetricKey = m.Key,
            Day = day.Key,
            Dimension = AllRepos,
            ActorKey = AllActors,
            Value = Math.Round(day.Value.Sum / day.Value.Count, 2),
            ComputedAt = now,
        })));

        await db.DailyMetrics.ExecuteDeleteAsync(cancellationToken);
        db.DailyMetrics.AddRange(metrics);
        await db.SaveChangesAsync(cancellationToken);

        return new AggregateResult(metrics.Count);
    }

    private static void Bump(
        Dictionary<(string, DateOnly, string), (double Value, string? Label)> counts,
        (string, DateOnly, string) key,
        double value,
        string? label)
    {
        counts.TryGetValue(key, out var current);
        counts[key] = (current.Value + value, label ?? current.Label);
    }

    private async Task<List<DailyMetric>> BuildReviewLatencyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var firstReviewByPr = await db.CanonicalReviews.AsNoTracking()
            .Where(r => r.SubmittedAt != null)
            .GroupBy(r => new { r.RepositoryFullName, r.PullRequestNumber })
            .Select(g => new { g.Key.RepositoryFullName, g.Key.PullRequestNumber, First = g.Min(r => r.SubmittedAt) })
            .ToDictionaryAsync(x => (x.RepositoryFullName, x.PullRequestNumber), x => x.First, cancellationToken);

        var pullRequests = await db.CanonicalPullRequests.AsNoTracking()
            .Where(p => p.CreatedAt != null)
            .ToListAsync(cancellationToken);

        var perDay = new Dictionary<DateOnly, (double SumHours, int Count)>();
        foreach (var pr in pullRequests)
        {
            if (!firstReviewByPr.TryGetValue((pr.RepositoryFullName, pr.Number), out var firstReview) || firstReview is null)
            {
                continue;
            }

            var hours = (firstReview.Value - pr.CreatedAt!.Value).TotalHours;
            if (hours < 0)
            {
                continue;
            }

            var day = DateOnly.FromDateTime(pr.CreatedAt.Value.UtcDateTime);
            perDay.TryGetValue(day, out var acc);
            perDay[day] = (acc.SumHours + hours, acc.Count + 1);
        }

        return perDay.Select(kvp => new DailyMetric
        {
            MetricKey = "pr_first_review_hours",
            Day = kvp.Key,
            Dimension = AllRepos,
            ActorKey = AllActors,
            Value = Math.Round(kvp.Value.SumHours / kvp.Value.Count, 2),
            ComputedAt = now,
        }).ToList();
    }

    private async Task<ActorLookup> BuildActorLookupAsync(CancellationToken cancellationToken)
    {
        var identities = await db.SourceIdentities.AsNoTracking()
            .Select(i => new { i.Id, i.PersonId, i.Login })
            .ToListAsync(cancellationToken);

        var people = await db.People.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);

        var map = identities.ToDictionary(
            i => i.Id,
            i => i.PersonId is { } personId && people.TryGetValue(personId, out var name)
                ? ($"person:{personId}", name)
                : ($"identity:{i.Id}", i.Login));

        return new ActorLookup(map);
    }

    private sealed class ActorLookup(Dictionary<Guid, (string Key, string Label)> map)
    {
        public (string Key, string? Label) Resolve(Guid? identityId)
        {
            if (identityId is { } id && map.TryGetValue(id, out var actor))
            {
                return actor;
            }

            return ("identity:unknown", "unknown");
        }
    }
}
