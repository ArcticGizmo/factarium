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
    private const string SourceGitHub = "github";
    private const string SourceJira = "jira";
    private const string GitHubIntegrationName = "GitHub (sample data)";
    private const string JiraIntegrationName = "Jira (sample data)";
    private const string Owner = "factarium-sample";
    private const string JiraBaseUrl = "https://factarium-sample.atlassian.net";
    private const string SourceClaude = "claude-code";
    private const string ClaudeIntegrationName = "Claude Code (OTEL)";

    private static readonly string[] Models = ["claude-opus-4-8", "claude-sonnet-5", "claude-haiku-4-5"];
    private static readonly string[] ProjectKeys = ["QAI", "OPS", "WEB"];
    private static readonly string[] IssueTypes = ["Story", "Bug", "Task"];
    private static readonly string[] IssueStatuses = ["To Do", "In Progress", "In Review", "Done"];

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

    public async Task<SampleSeedResult> SeedAsync(SampleSeedOptions options, CancellationToken cancellationToken)
    {
        var github = await EnsureIntegrationAsync(
            new GitHubIntegration { Name = GitHubIntegrationName, Org = Owner }, cancellationToken);

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

            facts.Add(Fact(SourceGitHub, "repository", repoId.ToString(), new
            {
                id = repoId,
                full_name = fullName,
                name = repoName,
                owner_id = 1,
                owner_login = Owner,
                description = $"Sample repository {repoName}",
                html_url = $"https://github.com/{fullName}",
                default_branch = "main",
                language = "C#",
                visibility = "public",
                stargazers_count = rng.Next(0, 400),
                forks_count = rng.Next(0, 60),
                open_issues_count = rng.Next(0, 30),
                pushed_at = Iso(now.AddDays(-rng.Next(0, 5))),
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

                // Roughly a quarter of commits are pair-authored with Claude, so the
                // co-author extraction has something to show on the records page.
                var coAuthors = c % 4 == 0
                    ? new object[] { new { name = "Claude", email = "noreply@anthropic.com" } }
                    : Array.Empty<object>();

                facts.Add(Fact(SourceGitHub, "commit", $"{fullName}@{sha}", new
                {
                    sha,
                    repo = fullName,
                    tree_sha = Sha(rng),
                    parents = Array.Empty<string>(),
                    message = $"{PrTitles[rng.Next(PrTitles.Length)]} ({sha[..7]})",
                    author_id = 1000 + Array.IndexOf(People, person),
                    author_login = person.Login,
                    author_name = person.Name,
                    author_email = person.Email,
                    co_authors = coAuthors,
                    committed_at = Iso(when),
                    comment_count = 0,
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

                facts.Add(Fact(SourceGitHub, "pull_request", prId.ToString(), new
                {
                    id = prId,
                    number,
                    repository_full_name = fullName,
                    title = PrTitles[rng.Next(PrTitles.Length)],
                    state = isOpen ? "open" : "closed",
                    draft = false,
                    author_id = 1000 + Array.IndexOf(People, author),
                    author_login = author.Login,
                    base_ref = "main",
                    head_ref = $"feature/{number}",
                    head_repo = fullName,
                    created_at = Iso(created),
                    updated_at = Iso(updated),
                    closed_at = closed is null ? null : Iso(closed.Value),
                    merged_at = merged is null ? null : Iso(merged.Value),
                    merge_commit_sha = merged is null ? null : Sha(rng),
                    comment_count = rng.Next(0, 6),
                    review_comment_count = rng.Next(0, 10),
                    additions = rng.Next(1, 500),
                    deletions = rng.Next(0, 200),
                    changed_files = rng.Next(1, 20),
                    commit_count = rng.Next(1, 15),
                }, updated));

                var reviews = rng.Next(0, 4);
                for (var v = 0; v < reviews; v++)
                {
                    var reviewer = People[rng.Next(peopleCount)];
                    var submitted = created.AddHours(rng.Next(1, 72));
                    var reviewId = prId * 10 + v;
                    reviewCount++;

                    facts.Add(Fact(SourceGitHub, "review", reviewId.ToString(), new
                    {
                        id = reviewId,
                        repository_full_name = fullName,
                        pull_request_number = number,
                        reviewer_id = 1000 + Array.IndexOf(People, reviewer),
                        reviewer_login = reviewer.Login,
                        state = ReviewStates[rng.Next(ReviewStates.Length)],
                        submitted_at = Iso(submitted),
                        commit_id = Sha(rng),
                    }, submitted));
                }
            }
        }

        var written = await sink.WriteAsync(github.Id, facts, cancellationToken);

        // --- Jira issues (distinct identities for the same people) ---
        var jira = await EnsureIntegrationAsync(
            new JiraIntegration { Name = JiraIntegrationName, BaseUrl = JiraBaseUrl, Email = "dev@example.com" },
            cancellationToken);

        var issueFacts = new List<RawFact>();
        var issueCount = rng.Next(40, 80);
        for (var n = 1; n <= issueCount; n++)
        {
            var project = ProjectKeys[rng.Next(ProjectKeys.Length)];
            var assignee = People[rng.Next(peopleCount)];
            var created = now.AddDays(-rng.Next(0, options.Days)).AddHours(-rng.Next(0, 24));
            var status = IssueStatuses[rng.Next(IssueStatuses.Length)];
            var isDone = status == "Done";
            DateTimeOffset? resolved = isDone ? created.AddHours(rng.Next(4, 400)) : null;
            var updated = resolved ?? created.AddHours(rng.Next(1, 72));
            var issueId = 200_000 + n;

            issueFacts.Add(Fact(SourceJira, "issue", issueId.ToString(), new
            {
                id = issueId.ToString(),
                key = $"{project}-{n}",
                fields = new
                {
                    summary = PrTitles[rng.Next(PrTitles.Length)],
                    status = new { name = status, statusCategory = new { key = isDone ? "done" : "indeterminate" } },
                    assignee = new { accountId = $"acc-{assignee.Login}", displayName = assignee.Name },
                    issuetype = new { name = IssueTypes[rng.Next(IssueTypes.Length)] },
                    project = new { key = project },
                    created = Jira(created),
                    updated = Jira(updated),
                    resolutiondate = resolved is null ? null : Jira(resolved.Value),
                },
            }, updated));
        }

        written += await sink.WriteAsync(jira.Id, issueFacts, cancellationToken);

        // --- Claude Code OTEL usage metrics (raw as the push receiver would store them) ---
        var claude = await EnsureIntegrationAsync(
            new ClaudeIntegration { Name = ClaudeIntegrationName }, cancellationToken);

        var usageFacts = new List<RawFact>();
        for (var p = 0; p < peopleCount; p++)
        {
            var person = People[p];
            var activeDays = rng.Next(options.Days / 3, options.Days);
            for (var d = 0; d < activeDays; d++)
            {
                var when = now.AddDays(-rng.Next(0, options.Days)).AddHours(-rng.Next(0, 12));
                var model = Models[rng.Next(Models.Length)];
                var session = $"sess-{person.Login}-{d}";

                void Metric(string name, string? unit, double value, string? type = null)
                {
                    var attrs = new Dictionary<string, string>
                    {
                        ["user.email"] = person.Email,
                        ["user.id"] = person.Login,
                        ["session.id"] = session,
                        ["model"] = model,
                    };
                    if (type is not null)
                    {
                        attrs["type"] = type;
                    }

                    usageFacts.Add(Fact(SourceClaude, "metric", $"{person.Login}-{d}-{name}-{type}", new
                    {
                        name,
                        unit,
                        value,
                        timeUnixNano = when.ToUnixTimeMilliseconds() * 1_000_000L,
                        attributes = attrs,
                    }, when));
                }

                Metric("claude_code.cost.usage", "USD", Math.Round(rng.NextDouble() * 4 + 0.2, 2));
                Metric("claude_code.token.usage", "tokens", rng.Next(800, 24000), "input");
                Metric("claude_code.token.usage", "tokens", rng.Next(400, 12000), "output");
                Metric("claude_code.lines_of_code.count", "count", rng.Next(20, 600), "added");
                Metric("claude_code.lines_of_code.count", "count", rng.Next(5, 250), "removed");
                Metric("claude_code.session.count", "count", rng.Next(1, 4));
            }
        }

        written += await sink.WriteAsync(claude.Id, usageFacts, cancellationToken);

        return new SampleSeedResult(repoCount, prCount, commitCount, reviewCount, issueCount, usageFacts.Count, written);
    }

    private async Task<Integration> EnsureIntegrationAsync(
        Integration prototype, CancellationToken cancellationToken)
    {
        var existing = await db.Integrations.FirstOrDefaultAsync(i => i.Name == prototype.Name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        prototype.Id = Guid.NewGuid();
        prototype.Enabled = false;
        prototype.ScheduleCron = null;
        prototype.CreatedAt = clock.GetUtcNow();

        db.Integrations.Add(prototype);
        await db.SaveChangesAsync(cancellationToken);
        return prototype;
    }

    private static RawFact Fact(string source, string entityType, string sourceId, object payload, DateTimeOffset? updatedAt) =>
        new(source, entityType, sourceId, JsonSerializer.Serialize(payload), updatedAt);

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static string Jira(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff+0000");

    private static string Sha(Random rng)
    {
        Span<byte> bytes = stackalloc byte[20];
        rng.NextBytes(bytes);
        return Convert.ToHexStringLower(bytes);
    }
}
