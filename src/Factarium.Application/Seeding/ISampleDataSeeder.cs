namespace Factarium.Application.Seeding;

public sealed record SampleSeedOptions(
    int Seed = 1337,
    int Repositories = 3,
    int People = 6,
    int Days = 90);

public sealed record SampleSeedResult(
    int Repositories,
    int PullRequests,
    int Commits,
    int Reviews,
    int Issues,
    int RecordsWritten);

/// <summary>
/// Generates deterministic, GitHub- and Jira-shaped raw records so the whole
/// sync → transform → aggregate → render pipeline can be exercised without any
/// API calls, rate limits, or real credentials. Same options + seed => same data.
/// GitHub and Jira actors are distinct identities for the same people, so linking
/// them to a Person demonstrates cross-source attribution.
/// </summary>
public interface ISampleDataSeeder
{
    Task<SampleSeedResult> SeedAsync(SampleSeedOptions options, CancellationToken cancellationToken);
}
