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

        // Table-per-hierarchy: the concrete type lives in the existing "Type" column.
        builder.HasDiscriminator(x => x.Type)
            .HasValue<GitHubIntegration>(GitHubIntegration.TypeName)
            .HasValue<JiraIntegration>(JiraIntegration.TypeName)
            .HasValue<ClaudeIntegration>(ClaudeIntegration.TypeName);

        builder.Property(x => x.Type).HasMaxLength(64);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();

        builder.Property(x => x.ScheduleCron).HasMaxLength(256);
        builder.Property(x => x.CursorState).HasColumnType("jsonb");

        builder.Property(x => x.LastRunStatus)
            .HasConversion<string>()
            .HasMaxLength(32);
    }
}

internal sealed class GitHubIntegrationConfiguration : IEntityTypeConfiguration<GitHubIntegration>
{
    public void Configure(EntityTypeBuilder<GitHubIntegration> builder)
    {
        builder.Property(x => x.Org).HasMaxLength(256);
    }
}

internal sealed class JiraIntegrationConfiguration : IEntityTypeConfiguration<JiraIntegration>
{
    public void Configure(EntityTypeBuilder<JiraIntegration> builder)
    {
        builder.Property(x => x.BaseUrl).HasMaxLength(512);
        builder.Property(x => x.Email).HasMaxLength(256);
    }
}
