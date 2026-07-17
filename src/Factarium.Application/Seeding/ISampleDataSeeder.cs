namespace Factarium.Application.Seeding;

public sealed record SampleSeedOptions(
    int Seed = 1337,
    int Repositories = 3,
    int People = 6,
    int Days = 90);

public sealed record SampleSeedResult(
    Guid IntegrationId,
    int Repositories,
    int PullRequests,
    int Commits,
    int Reviews,
    int RecordsWritten);

/// <summary>
/// Generates deterministic, GitHub-shaped raw records so the whole
/// sync → transform → aggregate → render pipeline can be exercised without any
/// API calls, rate limits, or real credentials. Same options + seed => same data.
/// </summary>
public interface ISampleDataSeeder
{
    Task<SampleSeedResult> SeedGitHubAsync(SampleSeedOptions options, CancellationToken cancellationToken);
}
