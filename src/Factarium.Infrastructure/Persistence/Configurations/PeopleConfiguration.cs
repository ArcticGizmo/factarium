using Factarium.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Factarium.Infrastructure.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
    }
}

internal sealed class SourceIdentityConfiguration : IEntityTypeConfiguration<SourceIdentity>
{
    public void Configure(EntityTypeBuilder<SourceIdentity> builder)
    {
        builder.ToTable("source_identities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Login).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(128);
        builder.Property(x => x.DisplayName).HasMaxLength(256);

        builder.HasIndex(x => new { x.Source, x.Login }).IsUnique();
        builder.HasIndex(x => x.PersonId);

        builder.HasOne<Person>()
            .WithMany()
            .HasForeignKey(x => x.PersonId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
