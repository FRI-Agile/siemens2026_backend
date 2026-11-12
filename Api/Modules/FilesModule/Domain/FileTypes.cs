namespace FilesModule.Domain;

/// <summary>
/// The content types the module knows (docs/api/files/files.md §8): the extensions a file of each type goes by, and how
/// its bytes begin. The declared content type is only the client's word, so confirm holds the stored bytes to it, and a
/// download always ends in an extension of the type the bytes were checked against.
/// </summary>
/// <remarks>
/// A type missing here has no check its bytes could pass, so a file of it is never confirmed: a purpose that accepts a
/// new type has to add it here too (<c>FileTypesTests</c> holds the purposes to that).
/// </remarks>
internal static class FileTypes
{
    /// <summary>How many leading bytes <see cref="HeadMatches"/> looks at, and so how many confirm reads.</summary>
    public const int HeadLength = 1024;

    private delegate bool HeadCheck(ReadOnlySpan<byte> head);

    /// <summary>A known type: its extensions, the first the canonical one, and the check on its leading bytes.</summary>
    private sealed record FileType(string[] Extensions, HeadCheck Matches);

    private static readonly Dictionary<string, FileType> ByContentType =
        new Dictionary<string, FileType>(StringComparer.OrdinalIgnoreCase)
        {
            // PDF readers accept the header anywhere in the first 1024 bytes, and some generators put bytes before it.
            ["application/pdf"] = new([".pdf"], head => head.IndexOf("%PDF-"u8) >= 0),
            // Word 97–2003 is an OLE compound file.
            ["application/msword"] = new([".doc"], head => head.StartsWith(OleCompoundFile)),
            // OOXML and ODF are ZIP archives. The archive's parts are not opened: the signature is what tells a renamed
            // picture or executable apart, and ClamAV reads the whole archive.
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] =
                new([".docx"], head => head.StartsWith(ZipLocalFile)),
            ["application/vnd.oasis.opendocument.text"] = new([".odt"], head => head.StartsWith(ZipLocalFile)),
            // Text has no signature; a NUL byte is what a binary file has and text does not.
            ["text/plain"] = new([".txt"], head => !head.Contains((byte)0)),
            ["image/jpeg"] = new([".jpg", ".jpeg"], head => head.StartsWith(Jpeg)),
            ["image/png"] = new([".png"], head => head.StartsWith(Png)),
        };

    private static ReadOnlySpan<byte> OleCompoundFile => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static ReadOnlySpan<byte> ZipLocalFile => "PK\x03\x04"u8;
    private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Whether the module knows <paramref name="contentType"/>, so a file of it can ever be confirmed.</summary>
    /// <param name="contentType">The content type.</param>
    public static bool IsKnown(string contentType) => ByContentType.ContainsKey(contentType);

    /// <summary>The canonical extension of <paramref name="contentType"/>, with its dot.</summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>The extension, or <c>null</c> for a type the module does not know.</returns>
    public static string? Extension(string contentType) =>
        ByContentType.TryGetValue(contentType, out FileType? type) ? type.Extensions[0] : null;

    /// <summary>Whether a file's leading bytes are what a file of <paramref name="contentType"/> begins with.</summary>
    /// <param name="contentType">The declared content type.</param>
    /// <param name="head">The file's first bytes, up to <see cref="HeadLength"/>.</param>
    /// <returns><c>false</c> for a type the module does not know.</returns>
    public static bool HeadMatches(string contentType, ReadOnlySpan<byte> head) =>
        ByContentType.TryGetValue(contentType, out FileType? type)
        && type.Matches(head.Length > HeadLength ? head[..HeadLength] : head);

    /// <summary>
    /// The name a file is saved under on download: the uploaded name, with the type's extension appended unless it already
    /// ends in one. A PDF uploaded as <c>cv.exe</c> is saved as <c>cv.exe.pdf</c>, so it opens as what was checked and
    /// never as a program.
    /// </summary>
    /// <param name="fileName">The name the file was uploaded under.</param>
    /// <param name="contentType">Its content type, which confirm checked the bytes against.</param>
    public static string DownloadName(string fileName, string contentType)
    {
        // Windows drops trailing dots and spaces when saving, so "cv.exe. " would become "cv.exe".
        string name = fileName.TrimEnd('.', ' ');
        if (name.Length == 0)
            name = "subor";

        if (!ByContentType.TryGetValue(contentType, out FileType? type))
            return name;

        string extension = Path.GetExtension(name);
        return type.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? name : name + type.Extensions[0];
    }
}
