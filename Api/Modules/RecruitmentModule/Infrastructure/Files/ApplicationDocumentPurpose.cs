using Kernel.Files;

namespace RecruitmentModule.Infrastructure.Files;

/// <summary>
/// A file a candidate submits with an application — a CV, a cover letter, a certificate (docs/api/files/files.md §11):
/// PDF, Word (<c>.doc</c>, <c>.docx</c>) or a JPEG/PNG scan or photo, at most 10 MB. Staff open them, and the CV scoring
/// reads the CV (ADR-024), which for an image means reading the picture.
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
        "image/jpeg",
        "image/png",
    };

    public long MaxSizeBytes => 10L * 1024 * 1024;
}
