using FilesModule.Domain.Entity;
using FilesModule.Infrastructure.FileStorage;
using Kernel.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FilesModule.Infrastructure.Scanning;

/// <summary>
/// Scans confirmed files ClamAV has not cleared yet, oldest first (docs/api/files/files.md §10). A clean file gets its
/// <see cref="StoredFile.ScannedAt"/> and becomes downloadable; an infected one loses its object at once and is
/// released, so it never reaches staff.
/// </summary>
/// <remarks>
/// A file that cannot be scanned now — ClamAV still loading its signatures, the object unreadable — stays unscanned and
/// is tried on the next tick, so the API runs without ClamAV and uploads simply wait (aspire-dev-stack.md §3).
/// </remarks>
internal sealed partial class FileScanner(
    FilesDbContext db, IFileStorage storage, IMalwareScanner scanner, TimeProvider time, ILogger<FileScanner> logger)
    : IScheduledWorker
{
    /// <summary>Files scanned per tick; a larger backlog drains over the next ones.</summary>
    internal const int BatchSize = 20;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        List<StoredFile> unscanned = await db.Files
            .Where(f => f.ETag != null && f.ScannedAt == null)
            .OrderBy(f => f.UploadedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        int cleared = 0;
        foreach (StoredFile file in unscanned)
        {
            ScanVerdict verdict;
            try
            {
                Stream? content = await storage.OpenReadAsync(file.ObjectKey, ct);
                if (content is null)
                {
                    LogObjectMissing(logger, file.Purpose);
                    continue;
                }

                await using (content)
                    verdict = await scanner.ScanAsync(content, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Most often clamd still loading after a restart; every other file would fail the same way.
                LogScanFailed(logger, ex.GetType().Name);
                break;
            }

            if (verdict.Clean)
            {
                file.ScannedAt = time.GetUtcNow();
                cleared++;
                continue;
            }

            // The bytes go now rather than after the retention window: nobody may download them, and keeping malware
            // for a week buys nothing. The row is released like any other, so a reference to it reads as "no file"; if the
            // delete fails, the janitor's retention sweep removes the object with the row.
            try
            {
                await storage.DeleteAsync(file.ObjectKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                LogInfectedDeleteFailed(logger, ex.GetType().Name);
            }

            file.DeletedAt = time.GetUtcNow();
            LogInfected(logger, file.Purpose, verdict.Signature ?? "unknown");
        }

        await db.SaveChangesAsync(ct);
        if (cleared > 0)
            LogCleared(logger, cleared);

        return cleared;
    }

    [LoggerMessage(EventId = 4100, EventName = "files.scan.cleared", Level = LogLevel.Information,
        Message = "Malware scan: {Cleared} files clean.")]
    private static partial void LogCleared(ILogger logger, int cleared);

    // No file id in these messages: an id is a record identifier, kept out of operational logs (ADR-A009). The rows
    // still unscanned are a query over files.file, not a log search.
    [LoggerMessage(EventId = 4101, EventName = "files.scan.infected", Level = LogLevel.Warning,
        Message = "Malware scan found {Signature} in a {Purpose} upload; its object was deleted and the file released.")]
    private static partial void LogInfected(ILogger logger, string purpose, string signature);

    [LoggerMessage(EventId = 4102, EventName = "files.scan.failed", Level = LogLevel.Warning,
        Message = "Malware scan failed ({FailureType}); the files stay unscanned until the next tick.")]
    private static partial void LogScanFailed(ILogger logger, string failureType);

    [LoggerMessage(EventId = 4104, EventName = "files.scan.infected_delete_failed", Level = LogLevel.Warning,
        Message = "Deleting an infected object failed ({FailureType}); the file is released and the janitor removes it later.")]
    private static partial void LogInfectedDeleteFailed(ILogger logger, string failureType);

    [LoggerMessage(EventId = 4103, EventName = "files.scan.object_missing", Level = LogLevel.Warning,
        Message = "A confirmed {Purpose} file has no object to scan; it stays unscanned.")]
    private static partial void LogObjectMissing(ILogger logger, string purpose);
}
