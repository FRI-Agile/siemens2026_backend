using FilesModule.Domain;
using FilesModule.Domain.Entity;
using FilesModule.Infrastructure.FileStorage;
using Kernel.Files;

namespace FilesModule.Infrastructure;

/// <summary>
/// Whether a pending file's object can be confirmed: the one check behind both the write's validation
/// (<see cref="FileReferences"/>) and the confirm the outbox triggers (<c>FileReferenceHandler</c>), so the two cannot
/// disagree about what a confirmable upload is.
/// </summary>
internal static class FileInspection
{
    private const string NotUploaded =
        "Súbor sa ešte nenahral. Počkajte na dokončenie nahrávania alebo ho nahrajte znova.";

    /// <summary>
    /// Reads the object's properties and leading bytes and holds them to the purpose. Everything checked at intent was
    /// the client's word about bytes that did not exist yet; a presigned URL bounds neither the size nor, on Azure, the
    /// content type, and no header proves what the bytes are.
    /// </summary>
    /// <param name="file">The pending file.</param>
    /// <param name="purpose">Its purpose; <c>null</c> when no module registers it, which fails closed.</param>
    /// <param name="storage">The object store.</param>
    /// <param name="ct">Token to cancel the call.</param>
    public static async Task<Inspection> InspectAsync(
        StoredFile file, IFilePurpose? purpose, IFileStorage storage, CancellationToken ct)
    {
        if (purpose is null)
            return Inspection.Refused("unknown_purpose", "Súbor nie je možné prijať. Nahrajte ho znova.");

        StoredObject? stored = await storage.StatAsync(file.ObjectKey, ct);
        if (stored is null)
            return Inspection.Refused("missing", NotUploaded);

        if (stored.SizeBytes > purpose.MaxSizeBytes)
            return Inspection.Refused("too_large",
                $"Súbor je väčší ako povolených {FileSize.Describe(purpose.MaxSizeBytes)}. Nahrajte menší súbor.");

        if (stored.SizeBytes == 0)
            return Inspection.Refused("empty", "Súbor je prázdny. Nahrajte ho znova.");

        if (!string.Equals(stored.ContentType, file.ContentType, StringComparison.OrdinalIgnoreCase)
            || !purpose.AllowedContentTypes.Contains(file.ContentType))
            return Inspection.Refused("content_type",
                "Typ nahraného súboru nezodpovedá ohlásenému typu. Nahrajte ho znova.");

        // The stored type is still only the header the client sent with its PUT; the bytes are what tell. The upload URL
        // cannot overwrite the blob, so the bytes read here are the bytes stat saw.
        byte[]? head = await storage.ReadHeadAsync(file.ObjectKey, FileTypes.HeadLength, ct);
        if (head is null)
            return Inspection.Refused("missing", NotUploaded);
        if (!FileTypes.HeadMatches(file.ContentType, head))
            return Inspection.Refused("signature",
                "Obsah súboru nezodpovedá jeho typu. Súbor môže byť poškodený alebo premenovaný. Nahrajte ho znova.");

        return new Inspection(stored, null, null);
    }
}

/// <summary>An inspection's outcome: the object, or why it is refused.</summary>
/// <param name="Stored">The object, when it can be confirmed.</param>
/// <param name="Rule">The broken rule's code for telemetry, when refused.</param>
/// <param name="Message">The Slovak message for the user, when refused.</param>
internal sealed record Inspection(StoredObject? Stored, string? Rule, string? Message)
{
    public static Inspection Refused(string rule, string message) => new(null, rule, message);
}
