namespace FilesModule.Features.FileFeatures.CreateUploadIntent;

/// <summary>The body of <c>POST /api/v1/files/uploads</c>: the file the client is about to upload.</summary>
public sealed record CreateUploadIntentRequest
{
    /// <summary>The kind of file, which decides the accepted content types and size, e.g. <c>application_document</c>.</summary>
    /// <example>application_document</example>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>The file's name as the user picked it; kept for display and as the downloaded file's name.</summary>
    /// <example>životopis.pdf</example>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The file's content type; the upload must send exactly this <c>Content-Type</c>.</summary>
    /// <example>application/pdf</example>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>The file's size in bytes.</summary>
    /// <example>482113</example>
    public long SizeBytes { get; init; }
}
