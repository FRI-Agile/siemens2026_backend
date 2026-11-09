using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Shared;

namespace Tests.FilesModule.Infrastructure.Scanning;

/// <summary>The scan between confirm and download: clean files become downloadable, infected ones go.</summary>
public class FileScannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Eicar = "EICAR"u8.ToArray();

    private static readonly FakeScanner ClamAv = new(bytes =>
        bytes.SequenceEqual(Eicar) ? new ScanVerdict(false, "Eicar-Test-Signature") : ScanVerdict.Passed);

    private static FileScanner Scanner(FilesDbContext db, FakeFileStorage storage, IMalwareScanner? scanner = null) =>
        new(db, storage, scanner ?? ClamAv, new FixedTime(Now), NullLogger<FileScanner>.Instance);

    [Fact]
    public async Task A_clean_file_is_stamped_and_an_infected_one_loses_its_object_and_is_released()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile clean = FileSeed.File(Now.AddMinutes(-3), etag: "a");
        StoredFile infected = FileSeed.File(Now.AddMinutes(-2), etag: "b");
        StoredFile pending = FileSeed.File(Now.AddMinutes(-1));
        await FileSeed.AddAsync(db, ct, clean, infected, pending);
        storage.Put(clean.ObjectKey, "%PDF-1.7"u8.ToArray(), "application/pdf");
        storage.Put(infected.ObjectKey, Eicar, "application/pdf");
        storage.Put(pending.ObjectKey, Eicar, "application/pdf");

        int cleared = await Scanner(db, storage).RunAsync(ct);

        Assert.Equal(1, cleared);
        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        Dictionary<Guid, StoredFile> stored = await read.Files.IgnoreQueryFilters().ToDictionaryAsync(f => f.Id, ct);
        Assert.Equal(Now, stored[clean.Id].ScannedAt);
        Assert.Equal((null, Now), (stored[infected.Id].ScannedAt, stored[infected.Id].DeletedAt));
        Assert.False(storage.Objects.ContainsKey(infected.ObjectKey));
        // Only a confirmed file is scanned: a pending one has nothing referencing it yet.
        Assert.Null(stored[pending.Id].ScannedAt);
        Assert.True(storage.Objects.ContainsKey(pending.ObjectKey));
    }

    [Fact]
    public async Task When_clamd_cannot_be_reached_the_files_stay_unscanned_for_the_next_tick()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile first = FileSeed.File(Now.AddMinutes(-2), etag: "a");
        StoredFile second = FileSeed.File(Now.AddMinutes(-1), etag: "b");
        await FileSeed.AddAsync(db, ct, first, second);
        storage.Put(first.ObjectKey, [1], "application/pdf");
        storage.Put(second.ObjectKey, [2], "application/pdf");
        FakeScanner down = new(_ => throw new System.Net.Sockets.SocketException());

        Assert.Equal(0, await Scanner(db, storage, down).RunAsync(ct));

        // The first failure stops the tick: every other file would fail the same way.
        Assert.Equal(1, down.Scans);
        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        Assert.All(await read.Files.ToListAsync(ct), f => Assert.Null(f.ScannedAt));
    }
}
