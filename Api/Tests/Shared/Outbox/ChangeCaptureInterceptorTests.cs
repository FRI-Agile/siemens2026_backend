using System.Text.Json;
using Kernel.Events;
using Kernel.Files;
using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.FilesModule;
using Tests.RecruitmentModule.Infrastructure;

namespace Tests.Shared.Outbox;

/// <summary>Change capture: a tracked entity's change and its outbox row commit together, attributed.</summary>
public class ChangeCaptureInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Staff = Guid.NewGuid();

    private sealed record Harness(string ConnectionString, RecruitmentDbContext Db, CountingSignal Signal, StubActor Actor);

    private static Harness Start(Actor? actor = null)
    {
        string connectionString = TestDb.NewDatabase();
        CountingSignal signal = new();
        StubActor stub = new(actor ?? Actor.Anonymous);
        ChangeCaptureInterceptor interceptor = new(stub, signal, new FixedTime(Now));
        return new Harness(connectionString, TestDb.NewRecruitmentContextOn(connectionString, interceptor), signal, stub);
    }

    private static async Task<List<OutboxMessage>> OutboxAsync(Harness h, CancellationToken ct)
    {
        await using RecruitmentDbContext read = TestDb.NewRecruitmentContextOn(h.ConnectionString);
        return await read.OutboxMessages.OrderBy(m => m.Id).ToListAsync(ct);
    }

    private static Guid? FileIdIn(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.GetProperty(nameof(ApplicationDocument.FileId)).GetGuid();

    [Fact]
    public async Task An_insert_of_a_tracked_entity_writes_one_attributed_row_and_untracked_ones_write_none()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start();
        await using RecruitmentDbContext db = h.Db;
        Application application = await OutboxSeed.ApplicationAsync(db, ct); // job post, candidate, application: untracked
        Assert.Empty(await OutboxAsync(h, ct));
        Assert.Equal(0, h.Signal.Notified);

        ApplicationDocument document = CandidateSeed.Document();
        document.ApplicationId = application.Id;
        db.ApplicationDocuments.Add(document);
        using (EventScope.Begin("application_submitted"))
            await db.SaveChangesAsync(ct);

        OutboxMessage row = Assert.Single(await OutboxAsync(h, ct));
        Assert.Equal((OutboxKind.RowChange, "recruitment", "application_document", document.Id.ToString(), ChangeAction.Insert),
            (row.Kind, row.Module, row.EntityType, row.EntityId, row.Action));
        Assert.Equal(("application_submitted", ActorTypes.Anonymous, (Guid?)null), (row.Event, row.ActorType, row.ActorUserId));
        Assert.Equal((Now, (short)1), (row.OccurredAt, row.Version));
        Assert.Null(row.OldValue);
        Assert.Equal(document.FileId, FileIdIn(row.NewValue));
        Assert.Equal(1, h.Signal.Notified);
    }

    [Fact]
    public async Task An_update_and_a_delete_carry_the_row_before_and_after()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start(Actor.ForUser(Staff));
        await using RecruitmentDbContext db = h.Db;
        Application application = await OutboxSeed.ApplicationAsync(db, ct);
        ApplicationDocument document = CandidateSeed.Document();
        document.ApplicationId = application.Id;
        db.ApplicationDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        Guid first = document.FileId;
        Guid second = Guid.CreateVersion7();

        document.FileId = second;
        await db.SaveChangesAsync(ct);
        db.ApplicationDocuments.Remove(document);
        await db.SaveChangesAsync(ct);

        List<OutboxMessage> rows = await OutboxAsync(h, ct);
        Assert.Equal(new[] { ChangeAction.Insert, ChangeAction.Update, ChangeAction.Delete }, rows.Select(r => r.Action));
        OutboxMessage update = rows[1], delete = rows[2];
        Assert.Equal((first, second, (short)2), (FileIdIn(update.OldValue), FileIdIn(update.NewValue), update.Version));
        Assert.Equal((second, (Guid?)null), (FileIdIn(delete.OldValue), FileIdIn(delete.NewValue)));
        Assert.All(rows, r => Assert.Equal((ActorTypes.User, (Guid?)Staff), (r.ActorType, r.ActorUserId)));
        Assert.Equal(EventScope.Unnamed, rows[0].Event); // saved outside any command
    }

    [Fact]
    public async Task A_failed_save_writes_no_row_and_wakes_nobody()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start();
        await using RecruitmentDbContext db = h.Db;
        ApplicationDocument orphan = CandidateSeed.Document();
        orphan.ApplicationId = Guid.NewGuid(); // no such application: the foreign key refuses the insert

        db.ApplicationDocuments.Add(orphan);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        Assert.Empty(await OutboxAsync(h, ct));
        Assert.Equal(0, h.Signal.Notified);
    }

    [Fact]
    public async Task Inside_a_transaction_the_dispatcher_is_woken_at_commit_and_not_after_a_rollback()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start();
        await using RecruitmentDbContext db = h.Db;
        Application application = await OutboxSeed.ApplicationAsync(db, ct);

        // A user-initiated transaction runs inside the execution strategy, as it must in the module's code.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var rolledBack = await db.Database.BeginTransactionAsync(ct);
            ApplicationDocument discarded = CandidateSeed.Document();
            discarded.ApplicationId = application.Id;
            db.ApplicationDocuments.Add(discarded);
            await db.SaveChangesAsync(ct);
            await rolledBack.RollbackAsync(ct);
        });

        db.ChangeTracker.Clear();
        Assert.Empty(await OutboxAsync(h, ct));
        Assert.Equal(0, h.Signal.Notified);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var committed = await db.Database.BeginTransactionAsync(ct);
            ApplicationDocument kept = CandidateSeed.Document();
            kept.ApplicationId = application.Id;
            db.ApplicationDocuments.Add(kept);
            await db.SaveChangesAsync(ct);
            Assert.Equal(0, h.Signal.Notified); // not visible to the dispatcher yet
            await committed.CommitAsync(ct);
        });

        Assert.Single(await OutboxAsync(h, ct));
        Assert.Equal(1, h.Signal.Notified);
    }

    [Fact]
    public async Task A_published_event_commits_with_the_save_and_is_stamped_but_keeps_its_name()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Harness h = Start(Actor.ForUser(Staff));
        await using RecruitmentDbContext db = h.Db;
        Guid[] files = [Guid.CreateVersion7(), Guid.CreateVersion7()];

        db.PublishFilesReleased(ModuleTypes.ModuleName, "candidate", "42", files);
        using (EventScope.Begin("candidate_erased"))
            await db.SaveChangesAsync(ct);

        OutboxMessage row = Assert.Single(await OutboxAsync(h, ct));
        Assert.Equal((OutboxKind.DomainEvent, FileRelease.EventName, "candidate", "42"), (row.Kind, row.Event, row.EntityType, row.EntityId));
        Assert.Equal((ActorTypes.User, (Guid?)Staff, Now), (row.ActorType, row.ActorUserId, row.OccurredAt));
        Assert.Equal(files, JsonSerializer.Deserialize<FilesReleased>(row.NewValue!)!.FileIds);
        Assert.Equal(1, h.Signal.Notified);
    }
}
