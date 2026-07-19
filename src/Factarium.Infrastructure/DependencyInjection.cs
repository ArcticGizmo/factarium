using Factarium.Application.Aggregate;
using Factarium.Application.Configuration;
using Factarium.Application.Pipeline;
using Factarium.Application.Seeding;
using Factarium.Application.Security;
using Factarium.Application.Sync;
using Factarium.Application.Transform;
using Factarium.Domain.Identity;
using Factarium.Infrastructure.Aggregate;
using Factarium.Infrastructure.Pipeline;
using Factarium.Infrastructure.Seeding;
using Factarium.Infrastructure.Transform;
using Factarium.Infrastructure.Persistence;
using Factarium.Infrastructure.Security;
using Factarium.Infrastructure.Sync;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Factarium.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Connection string name looked up in configuration first.</summary>
    public const string ConnectionStringName = "Factarium";

    public static IServiceCollection AddFactariumInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);
        services.AddDbContext<FactariumDbContext>(options => options.UseNpgsql(connectionString));

        services.TryAddSingleton(TimeProvider.System);

        services.Configure<DoraOptions>(configuration.GetSection(DoraOptions.SectionName));

        // Encrypt integration credentials at rest; key ring persists in Postgres
        // so tokens stay decryptable across restarts and shared-DB machines.
        services.AddDataProtection()
            .PersistKeysToDbContext<FactariumDbContext>()
            .SetApplicationName("Factarium");

        services.AddScoped<ICredentialProtector, DataProtectionCredentialProtector>();
        services.AddScoped<IRawRecordSink, EfRawRecordSink>();
        services.AddScoped<IRawRecordReader, EfRawRecordReader>();
        services.AddScoped<IIntegrationSyncService, IntegrationSyncService>();
        services.AddScoped<IPushIngestionService, PushIngestionService>();
        services.AddScoped<ISampleDataSeeder, SampleDataSeeder>();
        services.AddScoped<GitHubTransformService>();
        services.AddScoped<JiraTransformService>();
        services.AddScoped<ClaudeOtelTransformService>();
        services.AddScoped<ITransformService, CompositeTransformService>();
        services.AddScoped<IAggregateService, DailyMetricsAggregateService>();
        services.AddScoped<IPipelineRunner, PipelineRunner>();

        return services;
    }

    public static string ResolveConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
        ?? Environment.GetEnvironmentVariable("FACTARIUM_DB")
        ?? throw new InvalidOperationException(
            $"No connection string configured. Set ConnectionStrings:{ConnectionStringName} or the FACTARIUM_DB environment variable.");

    /// <summary>
    /// Applies pending migrations and seeds local defaults. Called on startup in
    /// dev-mode and by the CLI <c>db migrate</c> command.
    /// </summary>
    public static async Task MigrateAndSeedAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FactariumDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
        await SeedLocalUserAsync(db, cancellationToken);
    }

    private static async Task SeedLocalUserAsync(
        FactariumDbContext db,
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Id == LocalUser.Id, cancellationToken))
        {
            return;
        }

        db.Users.Add(new AppUser
        {
            Id = LocalUser.Id,
            UserName = LocalUser.UserName,
            DisplayName = LocalUser.DisplayName,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
