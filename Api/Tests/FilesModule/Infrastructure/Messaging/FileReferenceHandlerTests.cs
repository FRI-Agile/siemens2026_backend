using System.Text.Json;
using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.Messaging;
using Kernel.Files;
using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Shared;

namespace Tests.FilesModule.Infrastructure.Messaging;

/// <summary>A declared column gaining a file confirms it; losing one releases it; anything else is left alone.</summary>
public class FileReferenceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Staff = Guid.NewGuid();

    private static readonly FileReference Documents = new("recruitment", "application_document",
        [new FileColumn("FileId", [TestDocumentPurpose.PurposeKey])]);

    private static FileReferenceHandler Handler(FilesDbContext db, FakeFileStorage storage) =>
        new(db, storage, FileSeed.Purposes, [Documents], new FixedTime(Now), NullLogger<FileReferenceHandler>.Instance);

    private static EntityChange Change(ChangeAction action, Guid? before, Guid? after, Actor? actor = null) => new(
        Guid.CreateVersion7(), OutboxKind.RowChange, "recruitment", "test_event", "application_document", "1", action,
        before is null && action == ChangeAction.Insert ? null : Row(before),
        action == ChangeAction.Delete ? null : Row(after),
        actor ?? Actor.Anonymous, null, Now);

    private static string Row(Guid? fileId) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["FileId"] = fileId });

    private static async Task<StoredFile> ReadAsync(FilesDbContext db, Guid id, CancellationToken ct)
    {
        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        return await read.Files.IgnoreQueryFilters().SingleAsync(f => f.Id == id, ct);
    }

    [Fact]
    public void Matches_only_row_changes_of_a_declared_table()
    {
        FileReferenceHandler handler = new(null!, null!, FileSeed.Purposes, [Documents], TimeProvider.System,
            NullLogger<FileReferenceHandler>.Instance);

        Assert.True(handler.Matches(Change(ChangeAction.Insert, null, Guid.NewGuid())));
        Assert.False(handler.Matches(Change(ChangeAction.Insert, null, Guid.NewGuid()) with { EntityType = "candidate" }));
        Assert.False(handler.Matches(Change(ChangeAction.Insert, null, Guid.NewGuid()) with { Kind = OutboxKind.DomainEvent }));
    }

    [Fact]
    public async Task An_insert_confirms_the_file_with_its_real_size_and_redelivery_changes_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile file = FileSeed.File(Now.AddMinutes(-2));
        await FileSeed.AddAsync(db, ct, file);
        storage.Put(file.ObjectKey, FileContent.Pdf(640), "application/pdf");
        EntityChange insert = Change(ChangeAction.Insert, null, file.Id);

        await Handler(db, storage).HandleAsync([insert], ct);
        storage.Objects.Clear(); // a redelivery must not need the object again
        await Handler(db, storage).HandleAsync([insert], ct);

        StoredFile stored = await ReadAsync(db, file.Id, ct);
        Assert.Equal(("etag-640", 640L), (stored.ETag, stored.SizeBytes));
    }

    [Fact]
    public async Task A_file_whose_object_breaks_the_rules_or_is_of_another_purpose_stays_pending()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile oversize = FileSeed.File(Now.AddMinutes(-2));
        StoredFile photo = FileSeed.File(Now.AddMinutes(-2), purpose: TestPhotoPurpose.PurposeKey, contentType: "image/png");
        StoredFile missing = FileSeed.File(Now.AddMinutes(-2));
        await FileSeed.AddAsync(db, ct, oversize, photo, missing);
        storage.Put(oversize.ObjectKey, FileContent.Pdf(4096), "application/pdf");
        storage.Put(photo.ObjectKey, FileContent.Png(10), "image/png");

        await Handler(db, storage).HandleAsync(
            [Change(ChangeAction.Insert, null, oversize.Id), Change(ChangeAction.Insert, null, photo.Id),
             Change(ChangeAction.Insert, null, missing.Id)], ct);

        foreach (StoredFile file in new[] { oversize, photo, missing })
            Assert.Null((await ReadAsync(db, file.Id, ct)).ETag);
    }

    [Fact]
    public async Task Replacing_a_file_confirms_the_new_one_and_releases_the_old_one_as_the_actor()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile old = FileSeed.File(Now.AddDays(-1), etag: "old", scannedAt: Now.AddDays(-1));
        StoredFile replacement = FileSeed.File(Now.AddMinutes(-2));
        await FileSeed.AddAsync(db, ct, old, replacement);
        storage.Put(replacement.ObjectKey, FileContent.Pdf(10), "application/pdf");

        await Handler(db, storage).HandleAsync(
            [Change(ChangeAction.Update, old.Id, replacement.Id, Actor.ForUser(Staff))], ct);

        Assert.NotNull((await ReadAsync(db, replacement.Id, ct)).ETag);
        StoredFile released = await ReadAsync(db, old.Id, ct);
        Assert.Equal((Now, (Guid?)Staff), (released.DeletedAt, released.DeletedBy));
    }

    [Fact]
    public async Task A_delete_releases_the_file_once_and_never_a_file_of_another_purpose()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        StoredFile document = FileSeed.File(Now.AddDays(-1), etag: "a");
        StoredFile photo = FileSeed.File(Now.AddDays(-1), purpose: TestPhotoPurpose.PurposeKey, etag: "b");
        await FileSeed.AddAsync(db, ct, document, photo);
        FileReferenceHandler handler = Handler(db, new FakeFileStorage());

        await handler.HandleAsync([Change(ChangeAction.Delete, document.Id, null), Change(ChangeAction.Delete, photo.Id, null)], ct);
        DateTimeOffset firstRelease = (await ReadAsync(db, document.Id, ct)).DeletedAt!.Value;
        await new FileReferenceHandler(db, new FakeFileStorage(), FileSeed.Purposes, [Documents], new FixedTime(Now.AddHours(1)),
            NullLogger<FileReferenceHandler>.Instance).HandleAsync([Change(ChangeAction.Delete, document.Id, null)], ct);

        Assert.Equal(firstRelease, (await ReadAsync(db, document.Id, ct)).DeletedAt);
        Assert.Null((await ReadAsync(db, photo.Id, ct)).DeletedAt);
    }
}
