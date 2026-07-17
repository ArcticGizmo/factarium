using Factarium.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Minimal command dispatcher for developer tooling. Grows over later phases
// (`sync`, `snapshot`, `restore`, `seed`); Phase 0 ships `db migrate`.
var command = string.Join(' ', args.Take(2)).ToLowerInvariant();

switch (command)
{
    case "db migrate":
        return await RunDbMigrateAsync(args);

    case "":
    case "help":
    case "-h":
    case "--help":
        PrintUsage();
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command: '{command}'.");
        PrintUsage();
        return 1;
}

static async Task<int> RunDbMigrateAsync(string[] args)
{
    // Anchor the content root to the executable's directory so the bundled
    // appsettings.json is found regardless of the caller's working directory.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    builder.Services.AddFactariumInfrastructure(builder.Configuration);
    using var host = builder.Build();

    Console.WriteLine("Applying migrations and seeding local defaults...");
    await host.Services.MigrateAndSeedAsync();
    Console.WriteLine("Database is up to date.");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Factarium CLI

        Usage:
          factarium db migrate     Apply pending EF Core migrations and seed local defaults.
          factarium help           Show this help.

        Configuration:
          Connection string is read from ConnectionStrings:Factarium or the
          FACTARIUM_DB environment variable.
        """);
}
