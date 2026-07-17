using Factarium.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Factarium.Infrastructure.Persistence.Configurations;

internal sealed class RawRecordConfiguration : IEntityTypeConfiguration<RawRecord>
{
    public void Configure(EntityTypeBuilder<RawRecord> builder)
    {
        builder.ToTable("raw_records");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SourceId).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();

        // One row per source record per integration; re-sync upserts on this key.
        builder.HasIndex(x => new { x.IntegrationId, x.EntityType, x.SourceId }).IsUnique();

        // Transforms sweep by type across a source.
        builder.HasIndex(x => new { x.Source, x.EntityType });

        builder.HasOne<Integration>()
            .WithMany()
            .HasForeignKey(x => x.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
