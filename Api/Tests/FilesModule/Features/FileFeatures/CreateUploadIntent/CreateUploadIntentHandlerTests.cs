using FilesModule.Infrastructure.Scheduling;
using FilesModule.Domain.Entity;
using FilesModule.Features.FileFeatures.CreateUploadIntent;
using FilesModule.Infrastructure;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using Tests.Shared;
using Tests.Shared.Auth;

namespace Tests.FilesModule.Features.FileFeatures.CreateUploadIntent;

/// <summary>An upload intent: checked against the purpose, recorded pending, answered with a presigned URL.</summary>
public class CreateUploadIntentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static CreateUploadIntentHandler Handler(FilesDbContext db, Guid? user = null) =>
        new(db, new FakeFileStorage(), FileSeed.Purposes, new StubCurrentUser(user), new FixedTime(Now));

    private static CreateUploadIntentCommand Command(
        string purpose = TestDocumentPurpose.PurposeKey, string contentType = "application/pdf", long size = 500) =>
        new() { Purpose = purpose, FileName = "Životopis Jána.pdf", ContentType = contentType, SizeBytes = size };

    [Fact]
    public async Task Records_a_pending_file_and_returns_where_to_upload_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();

        CreateUploadIntentResponse response = await Handler(db).HandleAsync(Command(), ct);

        await using FilesDbContext read = TestDb.NewContextOnSameDb<FilesDbContext>(db);
        StoredFile stored = await read.Files.SingleAsync(ct);
        Assert.Equal(response.FileId, stored.Id);
        Assert.Equal((TestDocumentPurpose.PurposeKey, "Životopis Jána.pdf", "application/pdf", 500L),
            (stored.Purpose, stored.FileName, stored.ContentType, stored.SizeBytes));
        Assert.Null(stored.ETag); // pending until a row references it
        Assert.Null(stored.UploadedBy); // an anonymous applicant
        Assert.Equal(Now + FileConstants.UploadUrlLifetime, response.ExpiresAt);
        Assert.Equal("application/pdf", response.UploadHeaders["Content-Type"]);
    }

    [Fact]
    public async Task The_object_key_is_built_from_the_id_and_the_content_type_never_the_file_name()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();

        CreateUploadIntentResponse response = await Handler(db).HandleAsync(
            Command() with { FileName = "../../etc/passwd.pdf?x=1" }, ct);

        StoredFile stored = await db.Files.SingleAsync(ct);
        Assert.Equal($"uploads/{response.FileId:N}.pdf", stored.ObjectKey);
    }

    [Fact]
    public async Task A_staff_member_is_recorded_as_the_uploader()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();
        Guid staff = Guid.NewGuid();

        await Handler(db, staff).HandleAsync(Command(), ct);

        Assert.Equal(staff, (await db.Files.SingleAsync(ct)).UploadedBy);
    }

    [Fact]
    public async Task An_unknown_purpose_is_a_validation_error()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();

        ValidationException error = await Assert.ThrowsAsync<ValidationException>(
            () => Handler(db).HandleAsync(Command(purpose: "nothing_registers_this"), ct));

        Assert.Contains(nameof(CreateUploadIntentCommand.Purpose), error.Errors.Keys);
        Assert.False(await db.Files.AnyAsync(ct));
    }

    [Fact]
    public async Task A_content_type_and_a_size_the_purpose_refuses_are_both_reported()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using FilesDbContext db = TestDb.NewFilesContext();

        ValidationException error = await Assert.ThrowsAsync<ValidationException>(
            () => Handler(db).HandleAsync(Command(contentType: "text/html", size: 2048), ct));

        Assert.Equal(
            [nameof(CreateUploadIntentCommand.ContentType), nameof(CreateUploadIntentCommand.SizeBytes)],
            error.Errors.Keys.Order());
        Assert.Equal("Súbor je väčší ako povolených 1 kB.", error.Errors[nameof(CreateUploadIntentCommand.SizeBytes)].Single());
        Assert.False(await db.Files.AnyAsync(ct));
    }
}
