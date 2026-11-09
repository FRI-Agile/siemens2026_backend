using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Shared;

namespace Tests.FilesModule.Infrastructure.Scheduling;

/// <summary>The janitor reaps abandoned uploads and purges released files past retention, object first.</summary>
public class FileJanitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReapCutoff = Now - FileConstants.PendingRetention;
    private static readonly DateTimeOffset PurgeCutoff = Now - FileConstants.DeletedRetention;

    private static FileJanitor Janitor(FilesDbContext db, FakeFileStorage storage) =>
        new(db, storage, new FixedTime(Now), NullLogger<FileJanitor>.Instance);

    [Fact]
    public async Task Removes_abandoned_and_expired_files_with_their_objects_and_keeps_the_rest()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile abandoned = FileSeed.File(ReapCutoff.AddSeconds(-1));
        StoredFile uploading = FileSeed.File(ReapCutoff.AddSeconds(1));
        StoredFile confirmedLongAgo = FileSeed.File(Now.AddDays(-30), etag: "a", scannedAt: Now.AddDays(-30));
        StoredFile expired = FileSeed.File(Now.AddDays(-30), etag: "b", deletedAt: PurgeCutoff.AddSeconds(-1));
        StoredFile recentlyReleased = FileSeed.File(Now.AddDays(-30), etag: "c", deletedAt: PurgeCutoff.AddSeconds(1));
        StoredFile[] all = [abandoned, uploading, confirmedLongAgo, expired, recentlyReleased];
        await FileSeed.AddAsync(db, ct, all);
        foreach (StoredFile file in all)
            storage.Put(file.ObjectKey, [1], "application/pdf");

        Assert.Equal(2, await Janitor(db, storage).RunAsync(ct));

        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        Assert.Equal(
            new[] { uploading.Id, confirmedLongAgo.Id, recentlyReleased.Id }.Order(),
            (await read.Files.IgnoreQueryFilters().Select(f => f.Id).ToListAsync(ct)).Order());
        Assert.Equal(
            new[] { uploading.ObjectKey, confirmedLongAgo.ObjectKey, recentlyReleased.ObjectKey }.Order(),
            storage.Objects.Keys.Order());
    }

    [Fact]
    public async Task A_failed_object_delete_keeps_the_row_for_a_later_tick()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile stuck = FileSeed.File(ReapCutoff.AddMinutes(-1));
        StoredFile gone = FileSeed.File(ReapCutoff.AddMinutes(-2));
        await FileSeed.AddAsync(db, ct, stuck, gone);
        storage.FailingDeletes.Add(stuck.ObjectKey);

        Assert.Equal(1, await Janitor(db, storage).RunAsync(ct));

        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        Assert.Equal(stuck.Id, (await read.Files.SingleAsync(ct)).Id);
    }
}
