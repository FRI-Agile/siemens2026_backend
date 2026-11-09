using Kernel.Outbox;

namespace RecruitmentModule.Domain.Entity;

/// <summary>
/// A file submitted with an application (<c>recruitment.application_document</c>): what it is, and which file it is. The
/// file itself — name, type, size, bytes, scan — is the Files module's, referenced by <see cref="FileId"/>.
/// </summary>
/// <remarks>
/// Tracked, so its changes reach the outbox and the Files module confirms the file when a row gains it and releases it
/// when the row goes (docs/api/files/files.md §9). Written by the anonymous candidate and then by the system only, so no
/// <c>created_by</c> or <c>updated_by</c> (ADR-D008).
/// </remarks>
public sealed class ApplicationDocument : ITrackedEntity
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    /// <summary>One of <see cref="ApplicationDocumentTypes"/>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The <c>files.file</c> the document is; a logical reference, unique, with no foreign key.</summary>
    public Guid FileId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public short Version { get; set; }
}
