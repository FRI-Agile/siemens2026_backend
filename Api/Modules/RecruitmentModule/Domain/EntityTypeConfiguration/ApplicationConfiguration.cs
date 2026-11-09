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
        b.Property(a => a.CandidateId).HasColumnName("candidate_id");
        b.Property(a => a.Status).HasColumnName("status");

        b.HasOne<JobPost>().WithMany().HasForeignKey(a => a.JobPostId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(a => a.Candidate).WithMany().HasForeignKey(a => a.CandidateId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(a => a.Documents).WithOne().HasForeignKey(d => d.ApplicationId).OnDelete(DeleteBehavior.Cascade);

        // The column default and the audit trigger own these (conventions.md § Audit columns); no created_by (ADR-D008).
        b.Property(a => a.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at").ValueGeneratedOnUpdate();
        b.Property(a => a.UpdatedBy).HasColumnName("updated_by");
        b.Property(a => a.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
