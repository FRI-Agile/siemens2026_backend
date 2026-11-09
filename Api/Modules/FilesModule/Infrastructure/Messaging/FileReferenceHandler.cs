using System.Text.Json;
using FilesModule.Domain.Entity;
using FilesModule.Infrastructure.FileStorage;
using Kernel.Files;
using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FilesModule.Infrastructure.Messaging;

/// <summary>
/// Follows the file references other modules declared (<see cref="FileReference"/>) through their outbox changes
/// (docs/api/files/files.md §9). For each declared column it compares the row's file id before and after:
/// <list type="bullet">
/// <item><b>Gained</b> (an insert, or an update to another id): <b>confirm</b> the file — check its object and stamp its
/// ETag and real size. A file is confirmed only once a committed row points at it, so a write that failed confirms
/// nothing and the janitor reaps the upload.</item>
/// <item><b>Lost</b> (a delete, or an update to another id or none): <b>release</b> the file — soft-delete it, which stops
/// downloads at once; the janitor removes its bytes after the retention window.</item>
/// </list>
/// </summary>
/// <remarks>
/// Idempotent under redelivery: confirm only touches pending files and release only live ones. A file of a purpose the
/// column does not accept is left alone either way. An object that is missing or breaks its purpose's rules leaves the
/// file pending, to be reaped; that is logged, since the write's own validation should have caught it.
/// </remarks>
internal sealed partial class FileReferenceHandler(
    FilesDbContext db, IFileStorage storage, FilePurposeRegistry purposes, IEnumerable<FileReference> references,
    TimeProvider time, ILogger<FileReferenceHandler> logger) : IEntityChangeHandler
{
    private readonly Dictionary<(string Module, string EntityType), IReadOnlyList<FileColumn>> _columns =
        references.ToDictionary(r => (r.Module, r.EntityType), r => r.Columns);

    public bool Matches(EntityChange change) =>
        change.Kind == OutboxKind.RowChange && _columns.ContainsKey((change.Module, change.EntityType));

    public async Task HandleAsync(IReadOnlyList<EntityChange> changes, CancellationToken ct = default)
    {
        // A file reached from several columns takes the union of what they accept.
        Dictionary<Guid, HashSet<string>> gained = [];
        Dictionary<Guid, (HashSet<string> Accepted, Guid? By)> lost = [];

        foreach (EntityChange change in changes)
        {
            using JsonDocument? before = Parse(change.OldValue);
            using JsonDocument? after = Parse(change.NewValue);

            foreach (FileColumn column in _columns[(change.Module, change.EntityType)])
            {
                Guid? oldId = FileIdOf(before, column.Property);
                Guid? newId = FileIdOf(after, column.Property);

                if (newId is { } added && added != oldId)
                {
                    if (!gained.TryGetValue(added, out HashSet<string>? accepted))
                        gained[added] = accepted = new HashSet<string>(StringComparer.Ordinal);
                    accepted.UnionWith(column.Purposes);
                }

                if (oldId is { } removed && removed != newId)
                {
                    if (!lost.TryGetValue(removed, out var release))
                        lost[removed] = release = (new HashSet<string>(StringComparer.Ordinal), change.Actor.UserId);
                    release.Accepted.UnionWith(column.Purposes);
                }
            }
        }

        await ConfirmAsync(gained, ct);
        await ReleaseAsync(lost, ct);
    }

    private async Task ConfirmAsync(Dictionary<Guid, HashSet<string>> gained, CancellationToken ct)
    {
        if (gained.Count == 0)
            return;

        List<Guid> ids = [.. gained.Keys];
        List<StoredFile> pending = await db.Files.Where(f => ids.Contains(f.Id) && f.ETag == null).ToListAsync(ct);

        foreach (StoredFile file in pending)
        {
            if (!gained[file.Id].Contains(file.Purpose))
            {
                LogPurposeMismatch(logger, file.Purpose);
                continue;
            }

            // A storage error propagates, so the change is delivered again and the confirm retried.
            Inspection inspection = await FileInspection.InspectAsync(file, purposes.Find(file.Purpose), storage, ct);
            if (inspection.Stored is not { } stored)
            {
                LogNotConfirmed(logger, file.Purpose, inspection.Rule!);
                continue;
            }

            file.ETag = stored.ETag;
            file.SizeBytes = stored.SizeBytes;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ReleaseAsync(Dictionary<Guid, (HashSet<string> Accepted, Guid? By)> lost, CancellationToken ct)
    {
        if (lost.Count == 0)
            return;

        List<Guid> ids = [.. lost.Keys];
        // The query filter leaves out released files, so a redelivered release keeps the first one's time and author.
        List<StoredFile> live = await db.Files.Where(f => ids.Contains(f.Id)).ToListAsync(ct);

        DateTimeOffset now = time.GetUtcNow();
        foreach (StoredFile file in live)
        {
            (HashSet<string> accepted, Guid? by) = lost[file.Id];
            // A file of a kind this column never accepted was never its file, so clearing the column cannot delete it.
            if (!accepted.Contains(file.Purpose))
            {
                LogPurposeMismatch(logger, file.Purpose);
                continue;
            }

            file.DeletedAt = now;
            file.DeletedBy = by;
        }

        await db.SaveChangesAsync(ct);
    }

    private static JsonDocument? Parse(string? json) => json is null ? null : JsonDocument.Parse(json);

    private static Guid? FileIdOf(JsonDocument? row, string property) =>
        row is not null
        && row.RootElement.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetGuid(out Guid id)
            ? id
            : null;

    // No file id in these messages: a record identifier stays out of operational logs (ADR-A009).
    [LoggerMessage(EventId = 4300, EventName = "files.reference.purpose_mismatch", Level = LogLevel.Warning,
        Message = "A column referenced a {Purpose} file it does not accept; the file was left alone.")]
    private static partial void LogPurposeMismatch(ILogger logger, string purpose);

    [LoggerMessage(EventId = 4301, EventName = "files.reference.not_confirmed", Level = LogLevel.Warning,
        Message = "A referenced {Purpose} file was not confirmed ({Rule}); it stays pending and is reaped.")]
    private static partial void LogNotConfirmed(ILogger logger, string purpose, string rule);
}
