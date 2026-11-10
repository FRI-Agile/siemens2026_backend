using Kernel.Outbox;

namespace Kernel.Files;

/// <summary>
/// Releases files whose references disappear without a change the outbox captures: rows the database deletes by
/// <c>ON DELETE CASCADE</c>, or with <c>ExecuteDelete</c>. The producer publishes <see cref="EventName"/> in the same save
/// as that delete, and the Files module releases the files it names (docs/api/files/files.md §9).
/// </summary>
public static class FileRelease
{
    /// <summary>The event's name.</summary>
    public const string EventName = "files_released";

    /// <summary>Stages a <see cref="EventName"/> event on <paramref name="outbox"/>, committed by its next save.</summary>
    /// <param name="outbox">The producing module's context.</param>
    /// <param name="module">The producing module's name.</param>
    /// <param name="entityType">What was deleted, e.g. <c>application</c>.</param>
    /// <param name="entityId">Which one.</param>
    /// <param name="fileIds">Every file the deleted rows referenced; read them before the delete.</param>
    public static void PublishFilesReleased(
        this IOutboxDbContext outbox, string module, string entityType, string entityId, IReadOnlyCollection<Guid> fileIds)
    {
        if (fileIds.Count > 0)
            outbox.PublishEvent(module, EventName, entityType, entityId, new FilesReleased([.. fileIds]));
    }
}

/// <summary>The payload of <see cref="FileRelease.EventName"/>.</summary>
/// <param name="FileIds">The files no longer referenced.</param>
public sealed record FilesReleased(IReadOnlyList<Guid> FileIds);
