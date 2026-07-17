using Factarium.Domain.Metrics;
using Factarium.Domain.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Factarium.Infrastructure.Persistence.Configurations;

internal sealed class DailyMetricConfiguration : IEntityTypeConfiguration<DailyMetric>
{
    public void Configure(EntityTypeBuilder<DailyMetric> builder)
    {
        builder.ToTable("daily_metrics");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MetricKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Dimension).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ActorKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ActorLabel).HasMaxLength(256);

        builder.HasIndex(x => new { x.MetricKey, x.Day, x.Dimension, x.ActorKey }).IsUnique();
        builder.HasIndex(x => new { x.MetricKey, x.Day });
    }
}

internal sealed class PipelineStepConfiguration : IEntityTypeConfiguration<PipelineStep>
{
    public void Configure(EntityTypeBuilder<PipelineStep> builder)
    {
        builder.ToTable("pipeline_steps");
        builder.HasKey(x => x.Name);
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.LastStatus).HasMaxLength(32).IsRequired();
    }
}
