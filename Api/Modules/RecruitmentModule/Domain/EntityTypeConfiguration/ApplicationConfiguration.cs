using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Domain.EntityTypeConfiguration;

/// <summary>Maps <see cref="Application"/> onto <c>recruitment.application</c>, which the DDL script owns (ADR-D002).</summary>
internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> b)
    {
        b.ToTable("application", t => t.ExcludeFromMigrations());
        b.HasKey(a => a.Id);

        b.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(a => a.JobPostId).HasColumnName("job_post_id");
        b.Property(a => a.FirstName).HasColumnName("first_name");
        b.Property(a => a.LastName).HasColumnName("last_name");
        // A stored generated column: the database computes it from the name parts on every write.
        b.Property(a => a.DisplayName).HasColumnName("display_name").ValueGeneratedOnAddOrUpdate();
        b.Property(a => a.Email).HasColumnName("email");
        b.Property(a => a.PhoneNumber).HasColumnName("phone_number");
        b.Property(a => a.Status).HasColumnName("status");
        b.Property(a => a.IsManual).HasColumnName("is_manual");

        b.HasOne<JobPost>().WithMany().HasForeignKey(a => a.JobPostId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(a => a.Documents).WithOne().HasForeignKey(d => d.ApplicationId).OnDelete(DeleteBehavior.Cascade);

        // The column default and the audit trigger own these (conventions.md § Audit columns). created_by only on a
        // manual application (ADR-D011): a submitted one has no user to name (ADR-D008).
        b.Property(a => a.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
        b.Property(a => a.CreatedBy).HasColumnName("created_by");
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at").ValueGeneratedOnUpdate();
        b.Property(a => a.UpdatedBy).HasColumnName("updated_by");
        b.Property(a => a.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
