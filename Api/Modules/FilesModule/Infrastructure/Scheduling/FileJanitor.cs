using FilesModule.Domain.Entity;
using FilesModule.Infrastructure.FileStorage;
using Kernel.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FilesModule.Infrastructure.Scheduling;

/// <summary>
/// Reclaims storage in two passes (docs/api/files/files.md §10). <b>Reap:</b> a file still pending after
/// <see cref="FileConstants.PendingRetention"/> was never referenced, or its bytes never landed or broke its purpose's
/// rules, and can no longer be confirmed. <b>Retention sweep:</b> a file released longer than <see cref="FileConstants.DeletedRetention"/> ago has
/// served its grace period. Either way the object goes first and the row after it, so a storage failure leaves the row
/// for a later tick instead of an object nothing points at.
/// </summary>
internal sealed partial class FileJanitor(
    FilesDbContext db, IFileStorage storage, TimeProvider time, ILogger<FileJanitor> logger) : IScheduledWorker
{
    /// <summary>Rows handled per pass and tick; a larger backlog drains over the next ones, oldest first.</summary>
    internal const int BatchSize = 200;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = time.GetUtcNow();

        DateTimeOffset reapBefore = now - FileConstants.PendingRetention;
        List<StoredFile> abandoned = await db.Files
            .Where(f => f.ETag == null && f.UploadedAt < reapBefore)
            .OrderBy(f => f.UploadedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        DateTimeOffset purgeBefore = now - FileConstants.DeletedRetention;
        // Released rows are hidden by the query filter, so look past it.
        List<StoredFile> expired = await db.Files
            .IgnoreQueryFilters()
            .Where(f => f.DeletedAt != null && f.DeletedAt < purgeBefore)
            .OrderBy(f => f.DeletedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        int reaped = await RemoveAsync(abandoned, ct);
        int purged = await RemoveAsync(expired, ct);
        await db.SaveChangesAsync(ct);

        if (reaped + purged > 0)
            LogRemoved(logger, reaped, purged);

        return reaped + purged;
    }

    private async Task<int> RemoveAsync(List<StoredFile> files, CancellationToken ct)
    {
        int removed = 0;
        foreach (StoredFile file in files)
        {
            try
            {
                await storage.DeleteAsync(file.ObjectKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                LogDeleteFailed(logger, ex.GetType().Name);
                continue;
            }

            db.Files.Remove(file);
            removed++;
        }

        return removed;
    }

    [LoggerMessage(EventId = 4000, EventName = "files.janitor.removed", Level = LogLevel.Information,
        Message = "File janitor: {Reaped} abandoned uploads reaped, {Purged} released files purged.")]
    private static partial void LogRemoved(ILogger logger, int reaped, int purged);

    // No file id: an id is a record identifier, kept out of operational logs (ADR-A009). The row stays, so what is left
    // to remove is a query over files.file.
    [LoggerMessage(EventId = 4001, EventName = "files.janitor.delete_failed", Level = LogLevel.Warning,
        Message = "File janitor could not delete an object ({FailureType}); its row stays for a later tick.")]
    private static partial void LogDeleteFailed(ILogger logger, string failureType);
}
