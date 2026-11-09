namespace RecruitmentModule.Domain.Entity;

/// <summary>
/// A file submitted with an application (<c>recruitment.application_document</c>): its metadata, the blob being named
/// by <see cref="Id"/> in the module's container (ADR-A010).
/// </summary>
/// <remarks>Written by the anonymous candidate and then by the system only, so no <c>created_by</c> or <c>updated_by</c> (ADR-D008).</remarks>
public sealed class ApplicationDocument
{
    /// <summary>Also the blob's name.</summary>
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    /// <summary>One of <see cref="ApplicationDocumentTypes"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The name the file was uploaded under; shown to staff and used for downloads, never as the blob's name.</summary>
    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>One of <see cref="ApplicationDocumentStatuses"/>; staff see an <c>overený</c> (scanned clean) document only.</summary>
    public string Status { get; set; } = ApplicationDocumentStatuses.Uploading;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public short Version { get; set; }
}
