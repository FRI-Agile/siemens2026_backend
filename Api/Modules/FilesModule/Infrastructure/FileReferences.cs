using FilesModule.Infrastructure.Scheduling;
using FilesModule.Domain.Entity;
using FilesModule.Infrastructure.FileStorage;
using Kernel.Exceptions;
using Kernel.Files;
using Microsoft.EntityFrameworkCore;

namespace FilesModule.Infrastructure;

/// <summary>
/// The check a module makes before saving a reference to a file (docs/api/files/files.md §9). Changes nothing: the
/// confirm happens once the reference commits, when the outbox delivers it to <c>FileReferenceHandler</c>.
/// </summary>
internal sealed class FileReferences(
    FilesDbContext db, IFileStorage storage, FilePurposeRegistry purposes, TimeProvider time) : IFileReferences
{
    public async Task<ReferencedFile> ValidateAsync(
        Guid fileId, string field, IReadOnlyCollection<string> acceptedPurposes, CancellationToken ct = default)
    {
        StoredFile file = await db.Files.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, ct)
            ?? throw Refused(field, "Súbor sa nenašiel. Nahrajte ho znova.");

        // The purpose carries the content rules and the uploader chose it, so a column taking any purpose would let a
        // file into it under rules the column never agreed to. Checked before anything else, a confirmed file included.
        if (!acceptedPurposes.Contains(file.Purpose))
            throw Refused(field, "Tento súbor nie je určený pre toto pole. Nahrajte ho znova.");

        // Already confirmed: a retried write, which must pass the same way the first one did.
        if (file.ETag is not null)
            return Referenced(file, file.SizeBytes);

        // A reference has to land well inside the janitor's retention, so the outbox's confirm finds the file; past the
        // upload URL and its grace the client uploads again instead.
        if (file.UploadedAt < time.GetUtcNow() - FileConstants.UploadUrlLifetime - FileConstants.PendingGrace)
            throw Refused(field, "Platnosť nahrávania súboru vypršala. Nahrajte ho znova.");

        Inspection inspection = await FileInspection.InspectAsync(file, purposes.Find(file.Purpose), storage, ct);
        return inspection.Stored is { } stored
            ? Referenced(file, stored.SizeBytes)
            : throw Refused(field, inspection.Message!);
    }

    private static ReferencedFile Referenced(StoredFile file, long sizeBytes) =>
        new(file.Id, file.Purpose, file.FileName, file.ContentType, sizeBytes);

    private static ValidationException Refused(string field, string message) => ValidationException.ForField(field, message);
}
