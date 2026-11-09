using System.Text.Json;
using Kernel.Files;
using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace FilesModule.Infrastructure.Messaging;

/// <summary>
/// Releases the files named by a <see cref="FileRelease.EventName"/> event, which any module publishes for references
/// its outbox cannot capture, such as rows deleted by <c>ON DELETE CASCADE</c> (docs/api/files/files.md §9).
/// Idempotent: only live files are released.
/// </summary>
internal sealed class FileReleaseHandler(FilesDbContext db, TimeProvider time) : IEntityChangeHandler
{
    public bool Matches(EntityChange change) =>
        change.Kind == OutboxKind.DomainEvent && change.Event == FileRelease.EventName;

    public async Task HandleAsync(IReadOnlyList<EntityChange> changes, CancellationToken ct = default)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach (EntityChange change in changes)
        {
            FilesReleased released = JsonSerializer.Deserialize<FilesReleased>(change.NewValue ?? "{}")
                ?? throw new JsonException("A files_released event has no payload.");
            List<Guid> ids = [.. released.FileIds];

            await db.Files
                .Where(f => ids.Contains(f.Id))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(f => f.DeletedAt, now)
                    .SetProperty(f => f.DeletedBy, change.Actor.UserId), ct);
        }
    }
}
