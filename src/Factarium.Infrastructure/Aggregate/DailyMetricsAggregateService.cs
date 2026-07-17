using Factarium.Application.Aggregate;
using Factarium.Domain.Metrics;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Aggregate;

/// <summary>
/// Full-recompute aggregation: clears and rebuilds <c>daily_metrics</c> from the
/// canonical tables. Cheap for the single-Postgres model and trivially correct;
/// the pipeline's staleness gate decides <em>when</em> to run it.
/// </summary>
internal sealed class DailyMetricsAggregateService(FactariumDbContext db, TimeProvider clock) : IAggregateService
{
    private const string AllActors = "";
    private const string AllRepos = "";

    public async Task<AggregateResult> AggregateAsync(CancellationToken cancellationToken)
    {
        var actors = await BuildActorLookupAsync(cancellationToken);

        // (metricKey, day, actorKey) -> (value, label)
        var counts = new Dictionary<(string Metric, DateOnly Day, string ActorKey), (double Value, string? Label)>();

        void Add(string metric, DateOnly day, string actorKey, string? label, double value = 1)
        {
            var key = (metric, day, actorKey);
            counts.TryGetValue(key, out var current);
            counts[key] = (current.Value + value, label ?? current.Label);
        }

        void Count(string metric, DateTimeOffset? when, Guid? identityId)
        {
            if (when is null)
            {
                return;
            }

            var day = DateOnly.FromDateTime(when.Value.UtcDateTime);
            var (actorKey, label) = actors.Resolve(identityId);
            Add(metric, day, AllActors, null);
            Add(metric, day, actorKey, label);
        }

        var commits = await db.CanonicalCommits.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var commit in commits)
        {
            Count("commits", commit.CommittedAt, commit.AuthorIdentityId);
        }

        var pullRequests = await db.CanonicalPullRequests.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var pr in pullRequests)
        {
            Count("prs_opened", pr.CreatedAt, pr.AuthorIdentityId);
            if (pr.IsMerged)
            {
                Count("prs_merged", pr.MergedAt, pr.AuthorIdentityId);
            }
        }

        var now = clock.GetUtcNow();
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

        await db.DailyMetrics.ExecuteDeleteAsync(cancellationToken);
        db.DailyMetrics.AddRange(metrics);
        await db.SaveChangesAsync(cancellationToken);

        return new AggregateResult(metrics.Count);
    }

    private async Task<List<DailyMetric>> BuildReviewLatencyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        // First review per (repo, PR number).
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
            if (!firstReviewByPr.TryGetValue((pr.RepositoryFullName, pr.Number), out var firstReview)
                || firstReview is null)
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
