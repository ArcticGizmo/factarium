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
        builder.Property(x => x.Title).HasMaxLength(1024);
        builder.Property(x => x.IssueTypeId).HasMaxLength(64);
        builder.Property(x => x.IssueType).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(64);
        builder.Property(x => x.StatusCategory).HasMaxLength(64);
        builder.Property(x => x.AssigneeLogin).HasMaxLength(256);
        builder.Property(x => x.ReporterLogin).HasMaxLength(256);
        builder.Property(x => x.PrimaryDeveloperLogin).HasMaxLength(256);
        builder.Property(x => x.ClosedByLogin).HasMaxLength(256);
        builder.Property(x => x.SprintName).HasMaxLength(256);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.ClosedAt);
        builder.HasIndex(x => x.AssigneeIdentityId);
        builder.HasIndex(x => x.ReporterIdentityId);
        builder.HasIndex(x => x.PrimaryDeveloperIdentityId);
    }
}

internal sealed class CanonicalWorklogConfiguration : IEntityTypeConfiguration<CanonicalWorklog>
{
    public void Configure(EntityTypeBuilder<CanonicalWorklog> builder)
    {
        builder.ToTable("canonical_worklogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.IssueKey).HasMaxLength(64);
        builder.Property(x => x.IssueExternalId).HasMaxLength(128);
        builder.Property(x => x.AuthorLogin).HasMaxLength(256);
        builder.Property(x => x.Description).HasMaxLength(2048);
        builder.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.AuthorIdentityId);
        builder.HasIndex(x => x.WorkDate);
        builder.HasIndex(x => x.IssueKey);
    }
}

internal sealed class CanonicalIssueSegmentConfiguration : IEntityTypeConfiguration<CanonicalIssueSegment>
{
    public void Configure(EntityTypeBuilder<CanonicalIssueSegment> builder)
    {
        builder.ToTable("canonical_issue_segments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IssueKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IssueExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(128);
        builder.Property(x => x.Category).HasMaxLength(64);
        builder.Property(x => x.AssigneeLogin).HasMaxLength(256);
        builder.HasIndex(x => new { x.Source, x.IssueExternalId, x.Kind, x.StartedAt }).IsUnique();
        builder.HasIndex(x => new { x.Source, x.IssueKey });
        builder.HasIndex(x => x.AssigneeIdentityId);
        builder.HasIndex(x => new { x.Kind, x.Category });
    }
}

internal sealed class CanonicalIssueSprintMembershipConfiguration : IEntityTypeConfiguration<CanonicalIssueSprintMembership>
{
    public void Configure(EntityTypeBuilder<CanonicalIssueSprintMembership> builder)
    {
        builder.ToTable("canonical_issue_sprint_memberships");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IssueKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IssueExternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SprintName).HasMaxLength(256);
        builder.HasIndex(x => new { x.Source, x.IssueExternalId, x.SprintId, x.AddedAt }).IsUnique();
        builder.HasIndex(x => x.SprintId);
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
