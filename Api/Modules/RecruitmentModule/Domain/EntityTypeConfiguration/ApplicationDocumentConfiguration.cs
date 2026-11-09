using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Domain.EntityTypeConfiguration;

/// <summary>
/// Maps <see cref="ApplicationDocument"/> onto <c>recruitment.application_document</c>, which the DDL script owns
/// (ADR-D002).
/// </summary>
internal sealed class ApplicationDocumentConfiguration : IEntityTypeConfiguration<ApplicationDocument>
{
    public void Configure(EntityTypeBuilder<ApplicationDocument> b)
    {
        b.ToTable("application_document", t => t.ExcludeFromMigrations());
        b.HasKey(d => d.Id);

        b.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(d => d.ApplicationId).HasColumnName("application_id");
        b.Property(d => d.Type).HasColumnName("type");
        b.Property(d => d.FileId).HasColumnName("file_id");

        // The column default and the audit trigger own these; no created_by or updated_by (ADR-D008).
        b.Property(d => d.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
        b.Property(d => d.UpdatedAt).HasColumnName("updated_at").ValueGeneratedOnUpdate();
        b.Property(d => d.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
