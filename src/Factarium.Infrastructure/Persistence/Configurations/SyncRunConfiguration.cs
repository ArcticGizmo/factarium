using Factarium.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Factarium.Infrastructure.Persistence.Configurations;

internal sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.ToTable("sync_runs");
        builder.HasKey(x => x.Id);

        // Snapshotted from the integration at run time; lengths match "integrations".
        builder.Property(x => x.IntegrationName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.IntegrationType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(128);

        builder.Property(x => x.Trigger)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Per-integration history (the filtered view) and the default newest-first list.
        builder.HasIndex(x => new { x.IntegrationId, x.StartedAt });
        builder.HasIndex(x => x.StartedAt);

        // Removing a connection clears its run history, as with its raw records.
        builder.HasOne<Integration>()
            .WithMany()
            .HasForeignKey(x => x.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
