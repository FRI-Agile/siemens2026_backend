namespace Kernel.Files;

/// <summary>
/// What a module asks the Files module before saving a row that references a file. The reference itself confirms the
/// file once the save commits, through the outbox and the module's <see cref="FileReference"/>; losing it releases the
/// file the same way (docs/api/files/files.md §9). This check only lets a bad upload fail the write at once, with a
/// <c>400</c> the client can act on, instead of saving a reference to a file that will never be downloadable.
/// </summary>
public interface IFileReferences
{
    /// <summary>
    /// Checks, without changing anything, that the file a row is about to reference can be confirmed: it exists, has one
    /// of <paramref name="acceptedPurposes"/>, its upload window is open, and its object landed and meets its purpose's
    /// size and content-type rules. An already confirmed file of an accepted purpose passes, so a retried write succeeds.
    /// </summary>
    /// <param name="fileId">The file id the client sent.</param>
    /// <param name="field">The request field that carried it, for the validation error.</param>
    /// <param name="acceptedPurposes">The purposes the referencing column accepts: its <see cref="FileColumn.Purposes"/>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>What the file is, for the referencing module to show.</returns>
    /// <exception cref="Exceptions.ValidationException">
    /// The file cannot be confirmed — under <paramref name="field"/>, with a Slovak message asking to upload it again.
    /// </exception>
    Task<ReferencedFile> ValidateAsync(
        Guid fileId, string field, IReadOnlyCollection<string> acceptedPurposes, CancellationToken ct = default);
}

/// <summary>A file as <see cref="IFileReferences.ValidateAsync"/> found it.</summary>
/// <param name="Id">The file's id.</param>
/// <param name="Purpose">Its purpose's key.</param>
/// <param name="FileName">The name it was uploaded under, for display and download.</param>
/// <param name="ContentType">Its content type.</param>
/// <param name="SizeBytes">Its real size in bytes, as stored.</param>
public sealed record ReferencedFile(Guid Id, string Purpose, string FileName, string ContentType, long SizeBytes);
