using Factarium.Domain.Canonical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Factarium.Infrastructure.Persistence.Configurations;

internal sealed class CanonicalRepositoryConfiguration : IEntityTypeConfiguration<CanonicalRepository>
{
    public void Configure(EntityTypeBuilder<CanonicalRepository> builder)
    {
        builder.ToTable("canonical_repositories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Owner).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DefaultBranch).HasMaxLength(256);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
    }
}

internal sealed class CanonicalCommitConfiguration : IEntityTypeConfiguration<CanonicalCommit>
{
    public void Configure(EntityTypeBuilder<CanonicalCommit> builder)
    {
        builder.ToTable("canonical_commits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Sha).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RepositoryFullName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.AuthorLogin).HasMaxLength(256);
        builder.HasIndex(x => new { x.Source, x.RepositoryFullName, x.Sha }).IsUnique();
        builder.HasIndex(x => x.CommittedAt);
        builder.HasIndex(x => x.AuthorIdentityId);
    }
}

internal sealed class CanonicalPullRequestConfiguration : IEntityTypeConfiguration<CanonicalPullRequest>
{
    public void Configure(EntityTypeBuilder<CanonicalPullRequest> builder)
    {
        builder.ToTable("canonical_pull_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RepositoryFullName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(1024);
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
        builder.Property(x => x.BaseRef).HasMaxLength(256);
        builder.Property(x => x.AuthorLogin).HasMaxLength(256);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.MergedAt);
        builder.HasIndex(x => new { x.RepositoryFullName, x.Number });
    }
}

internal sealed class CanonicalUsageMetricConfiguration : IEntityTypeConfiguration<CanonicalUsageMetric>
{
    public void Configure(EntityTypeBuilder<CanonicalUsageMetric> builder)
    {
        builder.ToTable("canonical_usage_metrics");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.MetricKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActorLogin).HasMaxLength(256);
        builder.Property(x => x.SessionId).HasMaxLength(128);
        builder.Property(x => x.Model).HasMaxLength(128);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.MetricKey, x.OccurredAt });
        builder.HasIndex(x => x.ActorIdentityId);
    }
}

internal sealed class CanonicalIssueConfiguration : IEntityTypeConfiguration<CanonicalIssue>
{
    public void Configure(EntityTypeBuilder<CanonicalIssue> builder)
    {
        builder.ToTable("canonical_issues");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Key).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProjectKey).HasMaxLength(64);
        builder.Property(x => x.IssueType).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(64);
        builder.Property(x => x.AssigneeLogin).HasMaxLength(256);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.ResolvedAt);
        builder.HasIndex(x => x.AssigneeIdentityId);
    }
}

internal sealed class CanonicalReviewConfiguration : IEntityTypeConfiguration<CanonicalReview>
{
    public void Configure(EntityTypeBuilder<CanonicalReview> builder)
    {
        builder.ToTable("canonical_reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RepositoryFullName).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ReviewerLogin).HasMaxLength(256);
        builder.Property(x => x.State).HasMaxLength(32);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.RepositoryFullName, x.PullRequestNumber });
    }
}
