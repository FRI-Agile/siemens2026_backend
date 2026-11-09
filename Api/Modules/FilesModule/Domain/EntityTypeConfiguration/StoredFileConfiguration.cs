using FilesModule.Domain.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilesModule.Domain.EntityTypeConfiguration;

/// <summary>Maps <see cref="StoredFile"/> onto <c>files.file</c>, which the DDL script owns (ADR-D002).</summary>
internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("file", t => t.ExcludeFromMigrations());
        b.HasKey(f => f.Id);

        b.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(f => f.Purpose).HasColumnName("purpose");
        b.Property(f => f.ObjectKey).HasColumnName("object_key");
        b.Property(f => f.FileName).HasColumnName("file_name");
        b.Property(f => f.ContentType).HasColumnName("content_type");
        b.Property(f => f.SizeBytes).HasColumnName("size_bytes");
        b.Property(f => f.ETag).HasColumnName("etag");
        b.Property(f => f.ScannedAt).HasColumnName("scanned_at");
        b.Property(f => f.UploadedAt).HasColumnName("uploaded_at").ValueGeneratedOnAdd();
        b.Property(f => f.UploadedBy).HasColumnName("uploaded_by");
        b.Property(f => f.DeletedAt).HasColumnName("deleted_at");
        b.Property(f => f.DeletedBy).HasColumnName("deleted_by");

        // Live files only, by default: a released file is gone for every reader but the janitor.
        b.HasQueryFilter(f => f.DeletedAt == null);
    }
}
