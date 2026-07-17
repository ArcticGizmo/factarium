using Factarium.Domain.Canonical;
using Factarium.Domain.Identity;
using Factarium.Domain.Metrics;
using Factarium.Domain.People;
using Factarium.Domain.Pipeline;
using Factarium.Domain.Sync;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for Factarium's single-Postgres model. As the
/// data tiers grow (raw facts, canonical entities, materialized metrics) their
/// DbSets and configurations are added here and under <c>Configurations/</c>.
/// Also hosts the Data Protection key ring so encrypted credentials remain
/// decryptable across restarts and machines that share the database.
/// </summary>
public class FactariumDbContext(DbContextOptions<FactariumDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<Integration> Integrations => Set<Integration>();

    public DbSet<RawRecord> RawRecords => Set<RawRecord>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<SourceIdentity> SourceIdentities => Set<SourceIdentity>();

    public DbSet<CanonicalRepository> CanonicalRepositories => Set<CanonicalRepository>();

    public DbSet<CanonicalCommit> CanonicalCommits => Set<CanonicalCommit>();

    public DbSet<CanonicalPullRequest> CanonicalPullRequests => Set<CanonicalPullRequest>();

    public DbSet<CanonicalReview> CanonicalReviews => Set<CanonicalReview>();

    public DbSet<DailyMetric> DailyMetrics => Set<DailyMetric>();

    public DbSet<PipelineStep> PipelineSteps => Set<PipelineStep>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FactariumDbContext).Assembly);
    }
}
