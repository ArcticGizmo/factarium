using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Factarium.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core tools (<c>dotnet ef migrations</c>).
/// Reads the connection string from the <c>FACTARIUM_DB</c> environment variable,
/// falling back to the local docker-compose defaults.
/// </summary>
public sealed class FactariumDbContextFactory : IDesignTimeDbContextFactory<FactariumDbContext>
{
    public FactariumDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FACTARIUM_DB")
            ?? "Host=localhost;Port=6880;Database=factarium;Username=factarium;Password=factarium";

        var options = new DbContextOptionsBuilder<FactariumDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FactariumDbContext(options);
    }
}
