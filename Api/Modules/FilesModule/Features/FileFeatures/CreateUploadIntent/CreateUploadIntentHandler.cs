using System.ComponentModel.DataAnnotations;
using FilesModule.Infrastructure.Scheduling;
using FilesModule.Domain;
using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.FileStorage;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using Kernel.Files;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace FilesModule.Features.FileFeatures.CreateUploadIntent;

/// <summary>
/// Someone — an anonymous applicant or a staff member — is about to upload a file: record it pending and hand back a
/// URL to <c>PUT</c> the bytes to directly. The bytes never pass through the API.
/// </summary>
public sealed record CreateUploadIntentCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.UploadIntentCreated;

    /// <summary>An <c>IFilePurpose.Key</c> some module registered.</summary>
    [Required, StringLength(100)]
    public string Purpose { get; init; } = string.Empty;

    [Required, StringLength(255)]
    public string FileName { get; init; } = string.Empty;

    [Required, StringLength(255)]
    public string ContentType { get; init; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long SizeBytes { get; init; }
}

/// <summary>Where and how to upload, and the id to reference the file by once it is uploaded.</summary>
/// <param name="FileId">The pending file; the client puts it on the record the file belongs to, after the upload.</param>
/// <param name="UploadUrl">The presigned URL to <c>PUT</c> the bytes to.</param>
/// <param name="UploadHeaders">The headers the <c>PUT</c> must carry, exactly as given.</param>
/// <param name="ExpiresAt">When <paramref name="UploadUrl"/> stops working.</param>
public sealed record CreateUploadIntentResponse(
    Guid FileId, Uri UploadUrl, IReadOnlyDictionary<string, string> UploadHeaders, DateTimeOffset ExpiresAt);

internal sealed class CreateUploadIntentHandler(
    FilesDbContext db, IFileStorage storage, FilePurposeRegistry purposes, ICurrentUser currentUser, TimeProvider time)
    : ICommandHandler<CreateUploadIntentCommand, CreateUploadIntentResponse>
{
    public async Task<CreateUploadIntentResponse> HandleAsync(CreateUploadIntentCommand command, CancellationToken ct = default)
    {
        IFilePurpose purpose = purposes.Find(command.Purpose)
            ?? throw ValidationException.ForField(nameof(command.Purpose), "Neznámy druh súboru.");

        // The purpose's rules, not a global list. These check only what the client declares; confirm checks the stored
        // size, type and leading bytes (FileInspection).
        ValidationErrors errors = new();
        if (!purpose.AllowedContentTypes.Contains(command.ContentType))
            errors.Add(nameof(command.ContentType), "Tento typ súboru nie je povolený.");
        if (command.SizeBytes > purpose.MaxSizeBytes)
            errors.Add(nameof(command.SizeBytes),
                $"Súbor je väčší ako povolených {FileSize.Describe(purpose.MaxSizeBytes)}.");
        errors.ThrowIfAny();

        Guid id = Guid.CreateVersion7();
        // Server-owned, the extension included: it comes from the content type validated above, not the file name.
        string objectKey = ObjectKey.For(id, command.ContentType);
        DateTimeOffset expiresAt = time.GetUtcNow() + FileConstants.UploadUrlLifetime;

        PresignedUpload upload = await storage.CreateUploadUrlAsync(
            objectKey, command.ContentType, FileConstants.UploadUrlLifetime, ct);

        db.Files.Add(new StoredFile
        {
            Id = id,
            Purpose = purpose.Key,
            ObjectKey = objectKey,
            FileName = command.FileName,
            ContentType = command.ContentType,
            SizeBytes = command.SizeBytes, // declared; the real size replaces it on confirm
            UploadedBy = currentUser.Id, // null for an applicant: the endpoint is open to anyone
        });
        await db.SaveChangesAsync(ct);

        return new CreateUploadIntentResponse(id, upload.Url, upload.Headers, expiresAt);
    }
}
