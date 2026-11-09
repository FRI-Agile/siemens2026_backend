using System.Text.Json;
using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.Messaging;
using Kernel.Files;
using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Tests.Shared;

namespace Tests.FilesModule.Infrastructure.Messaging;

/// <summary><c>files_released</c> from any module releases the files it names.</summary>
public class FileReleaseHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static EntityChange Released(Actor actor, params Guid[] files) => new(
        Guid.CreateVersion7(), OutboxKind.DomainEvent, "recruitment", FileRelease.EventName, "candidate", "1",
        ChangeAction.Insert, null, JsonSerializer.Serialize(new FilesReleased(files)), actor, null, Now);

    [Fact]
    public async Task Releases_the_named_live_files()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        StoredFile named = FileSeed.File(Now.AddDays(-1), etag: "a");
        StoredFile other = FileSeed.File(Now.AddDays(-1), etag: "b");
        await FileSeed.AddAsync(db, ct, named, other);
        Guid staff = Guid.NewGuid();
        FileReleaseHandler handler = new(db, new FixedTime(Now));

        Assert.True(handler.Matches(Released(Actor.System)));
        await handler.HandleAsync([Released(Actor.ForUser(staff), named.Id, Guid.NewGuid())], ct);

        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        StoredFile released = await read.Files.IgnoreQueryFilters().SingleAsync(f => f.Id == named.Id, ct);
        Assert.Equal((Now, (Guid?)staff), (released.DeletedAt, released.DeletedBy));
        Assert.Null((await read.Files.SingleAsync(f => f.Id == other.Id, ct)).DeletedAt);
    }
}
