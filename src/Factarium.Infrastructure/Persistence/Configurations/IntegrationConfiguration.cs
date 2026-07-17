using Factarium.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Factarium.Infrastructure.Persistence.Configurations;

internal sealed class IntegrationConfiguration : IEntityTypeConfiguration<Integration>
{
    public void Configure(EntityTypeBuilder<Integration> builder)
    {
        builder.ToTable("integrations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();

        builder.Property(x => x.ScheduleCron).HasMaxLength(256);
        builder.Property(x => x.SettingsJson).HasColumnType("jsonb");
        builder.Property(x => x.CursorState).HasColumnType("jsonb");

        builder.Property(x => x.LastRunStatus)
            .HasConversion<string>()
            .HasMaxLength(32);
    }
}
