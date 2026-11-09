using FilesModule.Infrastructure.Scheduling;
using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using Kernel.Exceptions;
using Kernel.Files;
using Microsoft.EntityFrameworkCore;
using Tests.Shared;

namespace Tests.FilesModule.Infrastructure;

/// <summary>The check before a reference is saved: refuses what could never be confirmed, and changes nothing.</summary>
public class FileReferencesTests
{
    private const string Field = "CvFileId";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly string[] Documents = [TestDocumentPurpose.PurposeKey];

    private static FileReferences References(FilesDbContext db, FakeFileStorage storage) =>
        new(db, storage, FileSeed.Purposes, new FixedTime(Now));

    private static async Task<StoredFile> ReadAsync(FilesDbContext db, Guid id, CancellationToken ct)
    {
        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        return await read.Files.IgnoreQueryFilters().SingleAsync(f => f.Id == id, ct);
    }

    private static async Task<string> RefusedAsync(Func<Task> act)
    {
        ValidationException error = await Assert.ThrowsAsync<ValidationException>(act);
        return Assert.Single(error.Errors[Field]);
    }

    [Fact]
    public async Task A_landed_upload_passes_with_its_real_size_and_stays_pending()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile file = FileSeed.File(Now.AddMinutes(-2));
        await FileSeed.AddAsync(db, ct, file);
        storage.Put(file.ObjectKey, new byte[640], "application/pdf");

        ReferencedFile validated = await References(db, storage).ValidateAsync(file.Id, Field, Documents, ct);

        Assert.Equal(new ReferencedFile(file.Id, TestDocumentPurpose.PurposeKey, "životopis.pdf", "application/pdf", 640),
            validated);
        // The confirm is the outbox's, once the reference commits.
        Assert.Null((await ReadAsync(db, file.Id, ct)).ETag);
    }

    [Fact]
    public async Task A_confirmed_file_passes_again_without_asking_storage()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        StoredFile file = FileSeed.File(Now.AddDays(-1), etag: "abc");
        await FileSeed.AddAsync(db, ct, file);

        ReferencedFile validated = await References(db, new FakeFileStorage()).ValidateAsync(file.Id, Field, Documents, ct);

        Assert.Equal(file.Id, validated.Id);
    }

    [Fact]
    public async Task A_file_of_a_purpose_the_column_does_not_accept_is_refused_even_when_confirmed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        StoredFile file = FileSeed.File(Now.AddDays(-1), purpose: TestPhotoPurpose.PurposeKey, etag: "abc");
        await FileSeed.AddAsync(db, ct, file);

        Assert.Equal("Tento súbor nie je určený pre toto pole. Nahrajte ho znova.",
            await RefusedAsync(() => References(db, new FakeFileStorage()).ValidateAsync(file.Id, Field, Documents, ct)));
    }

    [Fact]
    public async Task An_unknown_or_released_file_is_refused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        StoredFile released = FileSeed.File(Now.AddDays(-1), etag: "abc", deletedAt: Now.AddHours(-1));
        await FileSeed.AddAsync(db, ct, released);
        FileReferences references = References(db, new FakeFileStorage());

        Assert.Equal("Súbor sa nenašiel. Nahrajte ho znova.",
            await RefusedAsync(() => references.ValidateAsync(Guid.NewGuid(), Field, Documents, ct)));
        Assert.Equal("Súbor sa nenašiel. Nahrajte ho znova.",
            await RefusedAsync(() => references.ValidateAsync(released.Id, Field, Documents, ct)));
    }

    [Fact]
    public async Task A_file_past_its_upload_window_is_refused_even_if_its_bytes_landed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile file = FileSeed.File(Now - FileConstants.UploadUrlLifetime - FileConstants.PendingGrace - TimeSpan.FromSeconds(1));
        await FileSeed.AddAsync(db, ct, file);
        storage.Put(file.ObjectKey, new byte[10], "application/pdf");

        Assert.Equal("Platnosť nahrávania súboru vypršala. Nahrajte ho znova.",
            await RefusedAsync(() => References(db, storage).ValidateAsync(file.Id, Field, Documents, ct)));
    }

    public static TheoryData<int?, string, string> BrokenRules => new()
    {
        { null, "application/pdf", "Súbor sa ešte nenahral. Počkajte na dokončenie nahrávania alebo ho nahrajte znova." },
        { 2048, "application/pdf", "Súbor je väčší ako povolených 1 kB. Nahrajte menší súbor." },
        { 0, "application/pdf", "Súbor je prázdny. Nahrajte ho znova." },
        // Azure binds no content type into the URL, so the stored type is what tells.
        { 10, "text/html", "Typ nahraného súboru nezodpovedá ohlásenému typu. Nahrajte ho znova." },
    };

    [Theory]
    [MemberData(nameof(BrokenRules))]
    public async Task What_landed_is_held_to_the_purpose(int? size, string storedType, string expected)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        FakeFileStorage storage = new();
        StoredFile file = FileSeed.File(Now.AddMinutes(-2));
        await FileSeed.AddAsync(db, ct, file);
        if (size is { } bytes)
            storage.Put(file.ObjectKey, new byte[bytes], storedType);

        Assert.Equal(expected, await RefusedAsync(() => References(db, storage).ValidateAsync(file.Id, Field, Documents, ct)));
    }
}
