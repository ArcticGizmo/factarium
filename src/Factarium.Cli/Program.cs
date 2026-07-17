using Factarium.Application.Seeding;
using Factarium.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Minimal command dispatcher for developer tooling. Grows over later phases
// (`snapshot`, `restore`); Phase 1 ships `db migrate` and `seed`.
var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

switch (command)
{
    case "db" when args.ElementAtOrDefault(1) == "migrate":
        return await RunDbMigrateAsync(args);

    case "seed":
        return await RunSeedAsync(args);

    case "help":
    case "-h":
    case "--help":
        PrintUsage();
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command: '{string.Join(' ', args)}'.");
        PrintUsage();
        return 1;
}

static async Task<int> RunDbMigrateAsync(string[] args)
{
    using var host = BuildHost(args);
    Console.WriteLine("Applying migrations and seeding local defaults...");
    await host.Services.MigrateAndSeedAsync();
    Console.WriteLine("Database is up to date.");
    return 0;
}

static async Task<int> RunSeedAsync(string[] args)
{
    var options = new SampleSeedOptions(
        Seed: IntArg(args, "--seed", 1337),
        Repositories: IntArg(args, "--repos", 3),
        People: IntArg(args, "--people", 6),
        Days: IntArg(args, "--days", 90));

    using var host = BuildHost(args);

    // Ensure schema exists before seeding, so `seed` works on a fresh database.
    await host.Services.MigrateAndSeedAsync();

    await using var scope = host.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<ISampleDataSeeder>();

    Console.WriteLine(
        $"Seeding sample GitHub data (seed={options.Seed}, repos={options.Repositories}, " +
        $"people={options.People}, days={options.Days})...");

    var result = await seeder.SeedGitHubAsync(options, CancellationToken.None);

    Console.WriteLine(
        $"Done. Integration {result.IntegrationId}: {result.Repositories} repos, " +
        $"{result.PullRequests} PRs, {result.Commits} commits, {result.Reviews} reviews " +
        $"({result.RecordsWritten} raw records written/updated).");
    return 0;
}

static IHost BuildHost(string[] args)
{
    // Anchor the content root to the executable's directory so the bundled
    // appsettings.json is found regardless of the caller's working directory.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Services.AddFactariumInfrastructure(builder.Configuration);
    return builder.Build();
}

static int IntArg(string[] args, string name, int fallback)
{
    var index = Array.IndexOf(args, name);
    if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value))
    {
        return value;
    }

    return fallback;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Factarium CLI

        Usage:
          factarium db migrate                 Apply pending EF Core migrations and seed local defaults.
          factarium seed [options]             Populate the database with deterministic sample GitHub data.
          factarium help                       Show this help.

        Seed options:
          --seed <int>     RNG seed (default 1337; same seed => same data)
          --repos <int>    Number of repositories (default 3)
          --people <int>   Number of contributors (default 6, max 8)
          --days <int>     History window in days (default 90)

        Configuration:
          Connection string is read from ConnectionStrings:Factarium or the
          FACTARIUM_DB environment variable.
        """);
}
