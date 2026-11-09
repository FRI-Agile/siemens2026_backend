using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Domain.EntityTypeConfiguration;

/// <summary>Maps <see cref="Candidate"/> onto <c>recruitment.candidate</c>, which the DDL script owns (ADR-D002).</summary>
internal sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> b)
    {
        b.ToTable("candidate", t => t.ExcludeFromMigrations());
        b.HasKey(c => c.Id);

        b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(c => c.FirstName).HasColumnName("first_name");
        b.Property(c => c.LastName).HasColumnName("last_name");
        // A stored generated column: the database computes it from the name parts on every write.
        b.Property(c => c.DisplayName).HasColumnName("display_name").ValueGeneratedOnAddOrUpdate();
        b.Property(c => c.Email).HasColumnName("email");
        b.Property(c => c.PhoneNumber).HasColumnName("phone_number");

        // The column default and the audit trigger own these (conventions.md § Audit columns); no created_by (ADR-D008).
        b.Property(c => c.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
        b.Property(c => c.UpdatedAt).HasColumnName("updated_at").ValueGeneratedOnUpdate();
        b.Property(c => c.UpdatedBy).HasColumnName("updated_by");
        b.Property(c => c.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
