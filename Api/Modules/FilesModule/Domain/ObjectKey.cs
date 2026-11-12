namespace FilesModule.Domain;

/// <summary>Builds the key an uploaded object is stored under. Nothing the client sends reaches it verbatim.</summary>
/// <remarks>
/// The extension is mapped from the content type, which the upload handler has already checked against the purpose,
/// rather than taken from the file name: <c>Path.GetExtension</c> returns whatever follows the last dot, so a name like
/// <c>cv.pdf?x=1</c> would carry a query delimiter into the key and from there into every presigned URL and log line
/// that shows it. The extension only helps someone browsing the container — the original name is kept on the row — so a
/// type <see cref="FileTypes"/> does not know simply yields a key without one.
/// </remarks>
internal static class ObjectKey
{
    /// <summary>The key a new upload is stored under.</summary>
    /// <param name="fileId">The pending file's id.</param>
    /// <param name="contentType">Its content type, already validated against the purpose.</param>
    /// <returns><c>uploads/{id}.ext</c>, or <c>uploads/{id}</c> for an unmapped type.</returns>
    public static string For(Guid fileId, string contentType) =>
        $"uploads/{fileId:N}{FileTypes.Extension(contentType)}";
}
