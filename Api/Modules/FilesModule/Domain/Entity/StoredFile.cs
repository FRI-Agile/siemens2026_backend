namespace FilesModule.Domain.Entity;

/// <summary>
/// One uploaded object's metadata (<c>files.file</c>). The bytes are in object storage under <see cref="ObjectKey"/>;
/// other modules reference the file by <see cref="Id"/>, a logical reference with no foreign key.
/// </summary>
/// <remarks>
/// The lifecycle has no status column (docs/api/files/files.md §2): <em>pending</em> while <see cref="ETag"/> is
/// <c>null</c>, <em>confirmed</em> once a row references it and the object checked out, <em>clean</em> once ClamAV
/// cleared it (<see cref="ScannedAt"/>), <em>released</em> once <see cref="DeletedAt"/> is set. Written by the
/// applicant's request and then by the system, so no <c>version</c> and no audit trigger.
/// </remarks>
public sealed class StoredFile
{
    public Guid Id { get; set; }

    /// <summary>The <c>IFilePurpose.Key</c> of the file's kind, which selects its content rules.</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>The blob's name in the module's container; server-generated, never the client's file name.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>The name the file was uploaded under, for display and the download's file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The declared content type; on confirm, the stored object must have exactly this one.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The declared size until confirm, then the object's real size.</summary>
    public long SizeBytes { get; set; }

    /// <summary>The object's ETag, stamped on confirm; <c>null</c> while the file is pending.</summary>
    public string? ETag { get; set; }

    /// <summary>When ClamAV found the confirmed object clean; <c>null</c> until then. Only a clean file is downloadable.</summary>
    public DateTimeOffset? ScannedAt { get; set; }

    /// <summary>When the upload URL was issued; set by the database.</summary>
    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>The staff member who asked for the upload URL; <c>null</c> for an anonymous applicant.</summary>
    public Guid? UploadedBy { get; set; }

    /// <summary>When the file lost its reference; <c>null</c> while it is live.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The staff member whose change released the file; <c>null</c> for the system or an applicant.</summary>
    public Guid? DeletedBy { get; set; }
}
