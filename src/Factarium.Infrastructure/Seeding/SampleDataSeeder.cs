using System.Text.Json;
using Factarium.Application.Seeding;
using Factarium.Application.Sync;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Seeding;

/// <summary>
/// Deterministic GitHub-shaped raw data generator. Produces repository, commit,
/// pull_request, and review records resembling the GitHub REST API, written to the
/// bronze tier under a dedicated "sample" integration. A given seed always yields
/// the same structure (repos, authors, counts, stable source ids), so re-seeding
/// upserts the same rows in place rather than duplicating them; timestamps are
/// anchored to the current time so the sample stays fresh relative to "today".
/// </summary>
internal sealed class SampleDataSeeder(
    FactariumDbContext db,
    IRawRecordSink sink,
    TimeProvider clock) : ISampleDataSeeder
{
    private const string Source = "github";
    private const string SampleIntegrationName = "GitHub (sample data)";
    private const string Owner = "factarium-sample";

    private static readonly (string Login, string Name, string Email)[] People =
    [
        ("octocat", "Mona Lisa", "octocat@example.com"),
        ("hubot", "Hubot", "hubot@example.com"),
        ("kepler", "Kepler Ada", "kepler@example.com"),
        ("noether", "Emmy Noether", "noether@example.com"),
        ("turing", "Alan Turing", "turing@example.com"),
        ("lovelace", "Ada Lovelace", "lovelace@example.com"),
        ("hopper", "Grace Hopper", "hopper@example.com"),
        ("ritchie", "Dennis Ritchie", "ritchie@example.com"),
    ];

    private static readonly string[] RepoNames =
        ["factarium", "metrics-worker", "dashboards", "sync-connectors", "docs-site", "cli-tools"];

    private static readonly string[] PrTitles =
    [
        "Fix flaky sync test", "Add GitHub connector", "Refactor aggregation worker",
        "Bump dependencies", "Improve dashboard rendering", "Handle rate limits",
        "Add snapshot command", "Tidy up logging", "Support incremental cursors",
        "Cache metric queries", "Wire up Quartz schedules", "Seed fake data",
    ];

    private static readonly string[] ReviewStates = ["APPROVED", "CHANGES_REQUESTED", "COMMENTED"];

    public async Task<SampleSeedResult> SeedGitHubAsync(SampleSeedOptions options, CancellationToken cancellationToken)
    {
        var integration = await EnsureIntegrationAsync(cancellationToken);

        var rng = new Random(options.Seed);
        var now = clock.GetUtcNow();
        var peopleCount = Math.Clamp(options.People, 1, People.Length);

        var facts = new List<RawFact>();
        int repoCount = 0, prCount = 0, commitCount = 0, reviewCount = 0;

        for (var r = 0; r < options.Repositories; r++)
        {
            var repoName = RepoNames[r % RepoNames.Length] + (r >= RepoNames.Length ? $"-{r}" : "");
            var repoId = 100_000 + r;
            var fullName = $"{Owner}/{repoName}";
            var repoCreated = now.AddDays(-options.Days - rng.Next(30, 400));
            repoCount++;

            facts.Add(Fact("repository", repoId.ToString(), new
            {
                id = repoId,
                node_id = $"R_{repoId}",
                name = repoName,
                full_name = fullName,
                @private = false,
                owner = new { login = Owner, id = 1, type = "Organization" },
                default_branch = "main",
                created_at = Iso(repoCreated),
                updated_at = Iso(now.AddDays(-rng.Next(0, 5))),
            }, repoCreated));

            // Commits
            var commits = rng.Next(30, 80);
            for (var c = 0; c < commits; c++)
            {
                var person = People[rng.Next(peopleCount)];
                var when = now.AddDays(-rng.Next(0, options.Days)).AddHours(-rng.Next(0, 24));
                var sha = Sha(rng);
                commitCount++;

                facts.Add(Fact("commit", $"{fullName}@{sha}", new
                {
                    sha,
                    commit = new
                    {
                        author = new { name = person.Name, email = person.Email, date = Iso(when) },
                        committer = new { name = person.Name, email = person.Email, date = Iso(when) },
                        message = $"{PrTitles[rng.Next(PrTitles.Length)]} ({sha[..7]})",
                    },
                    author = new { login = person.Login, id = 1000 + Array.IndexOf(People, person) },
                    repository_full_name = fullName,
                }, when));
            }

            // Pull requests + reviews
            var prs = rng.Next(8, 20);
            for (var p = 0; p < prs; p++)
            {
                var number = p + 1;
                var author = People[rng.Next(peopleCount)];
                var created = now.AddDays(-rng.Next(0, options.Days)).AddHours(-rng.Next(0, 24));
                var isMerged = rng.NextDouble() < 0.7;
                var isOpen = !isMerged && rng.NextDouble() < 0.3;
                DateTimeOffset? merged = isMerged ? created.AddHours(rng.Next(2, 120)) : null;
                DateTimeOffset? closed = isMerged ? merged : (isOpen ? null : created.AddHours(rng.Next(2, 200)));
                var prId = repoId * 1000 + number;
                var updated = merged ?? closed ?? created.AddHours(rng.Next(1, 48));
                prCount++;

                facts.Add(Fact("pull_request", prId.ToString(), new
                {
                    id = prId,
                    number,
                    title = PrTitles[rng.Next(PrTitles.Length)],
                    state = isOpen ? "open" : "closed",
                    user = new { login = author.Login, id = 1000 + Array.IndexOf(People, author) },
                    created_at = Iso(created),
                    updated_at = Iso(updated),
                    closed_at = closed is null ? null : Iso(closed.Value),
                    merged_at = merged is null ? null : Iso(merged.Value),
                    merge_commit_sha = merged is null ? null : Sha(rng),
                    @base = new { @ref = "main", repo = new { full_name = fullName, id = repoId } },
                    head = new { @ref = $"feature/{number}" },
                    repository_full_name = fullName,
                }, updated));

                var reviews = rng.Next(0, 4);
                for (var v = 0; v < reviews; v++)
                {
                    var reviewer = People[rng.Next(peopleCount)];
                    var submitted = created.AddHours(rng.Next(1, 72));
                    var reviewId = prId * 10 + v;
                    reviewCount++;

                    facts.Add(Fact("review", reviewId.ToString(), new
                    {
                        id = reviewId,
                        user = new { login = reviewer.Login, id = 1000 + Array.IndexOf(People, reviewer) },
                        state = ReviewStates[rng.Next(ReviewStates.Length)],
                        submitted_at = Iso(submitted),
                        pull_request_number = number,
                        repository_full_name = fullName,
                    }, submitted));
                }
            }
        }

        var written = await sink.WriteAsync(integration.Id, facts, cancellationToken);

        return new SampleSeedResult(integration.Id, repoCount, prCount, commitCount, reviewCount, written);
    }

    private async Task<Integration> EnsureIntegrationAsync(CancellationToken cancellationToken)
    {
        var integration = await db.Integrations
            .FirstOrDefaultAsync(i => i.Name == SampleIntegrationName, cancellationToken);

        if (integration is not null)
        {
            return integration;
        }

        integration = new Integration
        {
            Id = Guid.NewGuid(),
            Type = Source,
            Name = SampleIntegrationName,
            Enabled = false,
            ScheduleCron = null,
            SettingsJson = JsonSerializer.Serialize(new Dictionary<string, string?> { ["org"] = Owner }),
            CreatedAt = clock.GetUtcNow(),
        };

        db.Integrations.Add(integration);
        await db.SaveChangesAsync(cancellationToken);
        return integration;
    }

    private static RawFact Fact(string entityType, string sourceId, object payload, DateTimeOffset? updatedAt) =>
        new(Source, entityType, sourceId, JsonSerializer.Serialize(payload), updatedAt);

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static string Sha(Random rng)
    {
        Span<byte> bytes = stackalloc byte[20];
        rng.NextBytes(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}
