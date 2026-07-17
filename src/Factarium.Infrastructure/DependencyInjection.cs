using Factarium.Application.Security;
using Factarium.Domain.Identity;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
