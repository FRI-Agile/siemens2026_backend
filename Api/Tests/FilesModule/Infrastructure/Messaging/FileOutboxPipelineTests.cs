using FilesModule.Domain.Entity;
using FilesModule.Infrastructure;
using FilesModule.Infrastructure.FileStorage;
using FilesModule.Infrastructure.Messaging;
using Kernel.Files;
using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Infrastructure;
using Tests.Shared;
using Tests.Shared.Outbox;

namespace Tests.FilesModule.Infrastructure.Messaging;

/// <summary>
/// The whole path on one database: Recruitment saves a document row, change capture writes its outbox, the dispatcher
/// delivers it, and the Files module confirms the file; deleting the row, or publishing <c>files_released</c>, releases it.
/// </summary>
public class FileOutboxPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Staff = Guid.NewGuid();

    private sealed record Harness(
        string ConnectionString, RecruitmentDbContext Recruitment, OutboxDispatcher Dispatcher, FakeFileStorage Storage);

    private static Harness Start()
    {
        string connectionString = TestDb.NewDatabase();
        FixedTime time = new(Now);
        FakeFileStorage storage = new();
        CountingSignal signal = new();

        ServiceCollection services = new();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IFileStorage>(storage);
        services.AddSingleton(FileSeed.Purposes);
        services.AddSingleton(new FileReference(ModuleTypes.ModuleName, "application_document",
            [new FileColumn(nameof(ApplicationDocument.FileId), [TestDocumentPurpose.PurposeKey])]));
        services.AddScoped(_ => TestDb.NewFilesContextOn(connectionString));
        services.AddScoped<IOutboxDbContext>(_ => TestDb.NewRecruitmentContextOn(connectionString));
        services.AddScoped<IEntityChangeHandler, FileReferenceHandler>();
        services.AddScoped<IEntityChangeHandler, FileReleaseHandler>();

        OutboxDispatcher dispatcher = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), signal, time,
            NullLogger<OutboxDispatcher>.Instance);
        ChangeCaptureInterceptor capture = new(new StubActor(Actor.ForUser(Staff)), signal, time);
        return new Harness(connectionString, TestDb.NewRecruitmentContextOn(connectionString, capture), dispatcher, storage);
    }

    private static async Task<StoredFile> UploadedAsync(Harness h, CancellationToken ct)
    {
        await using FilesDbContext files = TestDb.NewFilesContextOn(h.ConnectionString);
        StoredFile file = FileSeed.File(Now.AddMinutes(-1));
        await FileSeed.AddAsync(files, ct, file);
        h.Storage.Put(file.ObjectKey, new byte[300], "application/pdf");
        return file;
    }

    private static async Task<StoredFile> FileAsync(Harness h, Guid id, CancellationToken ct)
    {
        await using FilesDbContext files = TestDb.NewFilesContextOn(h.ConnectionString);
        return await files.Files.IgnoreQueryFilters().SingleAsync(f => f.Id == id, ct);
    }

    [Fact]
    public async Task Saving_a_reference_confirms_the_file_and_deleting_it_releases_the_file()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start();
        await using RecruitmentDbContext db = h.Recruitment;
        Application application = await OutboxSeed.ApplicationAsync(db, ct);
        StoredFile file = await UploadedAsync(h, ct);

        ApplicationDocument document = ApplicationSeed.Document();
        document.ApplicationId = application.Id;
        document.FileId = file.Id;
        db.ApplicationDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        Assert.Null((await FileAsync(h, file.Id, ct)).ETag); // nothing happens until the outbox is delivered

        Assert.Equal(1, await h.Dispatcher.DispatchOnceAsync(ct));
        Assert.Equal(300, (await FileAsync(h, file.Id, ct)).SizeBytes);
        Assert.NotNull((await FileAsync(h, file.Id, ct)).ETag);

        db.ApplicationDocuments.Remove(document);
        await db.SaveChangesAsync(ct);
        await h.Dispatcher.DispatchOnceAsync(ct);

        StoredFile released = await FileAsync(h, file.Id, ct);
        Assert.Equal((Now, (Guid?)Staff), (released.DeletedAt, released.DeletedBy));
        Assert.False(await db.OutboxMessages.AnyAsync(ct));
    }

    [Fact]
    public async Task A_write_that_rolls_back_confirms_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start();
        await using RecruitmentDbContext db = h.Recruitment;
        Application application = await OutboxSeed.ApplicationAsync(db, ct);
        StoredFile file = await UploadedAsync(h, ct);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            ApplicationDocument document = ApplicationSeed.Document();
            document.ApplicationId = application.Id;
            document.FileId = file.Id;
            db.ApplicationDocuments.Add(document);
            await db.SaveChangesAsync(ct);
            await transaction.RollbackAsync(ct);
        });

        Assert.Equal(0, await h.Dispatcher.DispatchOnceAsync(ct));
        Assert.Null((await FileAsync(h, file.Id, ct)).ETag); // left for the janitor
    }

    [Fact]
    public async Task A_cascade_the_outbox_cannot_see_is_covered_by_files_released()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start();
        await using RecruitmentDbContext db = h.Recruitment;
        Application application = await OutboxSeed.ApplicationAsync(db, ct);
        StoredFile file = await UploadedAsync(h, ct);
        ApplicationDocument document = ApplicationSeed.Document();
        document.ApplicationId = application.Id;
        document.FileId = file.Id;
        db.ApplicationDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        await h.Dispatcher.DispatchOnceAsync(ct);
        db.ChangeTracker.Clear();

        // Deleting the application cascades to its documents in the database, past change capture; the feature reads the
        // documents' files first and publishes them in the same transaction as the delete.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            List<Guid> fileIds = await db.ApplicationDocuments
                .Where(d => d.ApplicationId == application.Id).Select(d => d.FileId).ToListAsync(ct);
            await db.Applications.Where(a => a.Id == application.Id).ExecuteDeleteAsync(ct);
            db.PublishFilesReleased(ModuleTypes.ModuleName, "application", application.Id.ToString(), fileIds);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        await h.Dispatcher.DispatchOnceAsync(ct);

        Assert.NotNull((await FileAsync(h, file.Id, ct)).DeletedAt);
    }
}
