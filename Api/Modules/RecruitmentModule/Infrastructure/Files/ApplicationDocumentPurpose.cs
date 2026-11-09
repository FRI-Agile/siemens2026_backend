using Kernel.Files;

namespace RecruitmentModule.Infrastructure.Files;

/// <summary>
/// A file a candidate submits with an application — a CV, a cover letter, a certificate (docs/api/files/files.md §11).
/// Documents only: what staff open and what the CV scoring reads (ADR-024).
/// </summary>
internal sealed class ApplicationDocumentPurpose : IFilePurpose
{
    public const string PurposeKey = "application_document";

    public string Key => PurposeKey;

    public IReadOnlySet<string> AllowedContentTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.oasis.opendocument.text",
    };

    public long MaxSizeBytes => 10L * 1024 * 1024;
}
