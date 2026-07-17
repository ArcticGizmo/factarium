using Factarium.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for Factarium's single-Postgres model. As the
/// data tiers grow (raw facts, canonical entities, materialized metrics) their
/// DbSets and configurations are added here and under <c>Configurations/</c>.
/// </summary>
public class FactariumDbContext(DbContextOptions<FactariumDbContext> options)
    : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FactariumDbContext).Assembly);
    }
}
