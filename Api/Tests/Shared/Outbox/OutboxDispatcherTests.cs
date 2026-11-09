using Kernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RecruitmentModule.Infrastructure;
using Tests.FilesModule;

namespace Tests.Shared.Outbox;

/// <summary>The dispatcher: deliver in order, delete what was delivered, retry what failed without holding up the rest.</summary>
public class OutboxDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(string ConnectionString, OutboxDispatcher Dispatcher, FixedTime Time);

    private static Harness Start(params IEntityChangeHandler[] handlers)
    {
        string connectionString = TestDb.NewDatabase();
        ServiceCollection services = new();
        services.AddScoped<IOutboxDbContext>(_ => TestDb.NewRecruitmentContextOn(connectionString));
        foreach (IEntityChangeHandler handler in handlers)
            services.AddSingleton(handler);

        FixedTime time = new(Now);
        OutboxDispatcher dispatcher = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new CountingSignal(), time,
            NullLogger<OutboxDispatcher>.Instance);
        return new Harness(connectionString, dispatcher, time);
    }

    private static async Task AddAsync(Harness h, CancellationToken ct, params OutboxMessage[] messages)
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContextOn(h.ConnectionString);
        db.OutboxMessages.AddRange(messages);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<OutboxMessage>> OutboxAsync(Harness h, CancellationToken ct)
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContextOn(h.ConnectionString);
        return await db.OutboxMessages.OrderBy(m => m.Id).ToListAsync(ct);
    }

    [Fact]
    public async Task Delivers_each_handler_its_changes_in_order_and_deletes_every_row()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RecordingHandler documents = new(c => c.EntityType == "application_document");
        RecordingHandler events = new(c => c.Kind == OutboxKind.DomainEvent);
        Harness h = Start(documents, events);
        OutboxMessage first = OutboxSeed.Message("1"), second = OutboxSeed.Message("2"), nobodys = OutboxSeed.Message("3", "candidate");
        await AddAsync(h, ct, first, second, nobodys);

        Assert.Equal(3, await h.Dispatcher.DispatchOnceAsync(ct));

        Assert.Equal(new[] { "1", "2" }, documents.Received.Select(c => c.EntityId));
        Assert.Equal((ActorTypes.System, "test_event"), (documents.Received[0].Actor.Type, documents.Received[0].Event));
        Assert.Empty(events.Received);
        Assert.Empty(await OutboxAsync(h, ct)); // a change nobody wants is delivered too: to no one
    }

    [Fact]
    public async Task A_failing_row_is_retried_later_and_the_rest_go_through()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RecordingHandler handler = new(_ => true) { FailFor = { "bad" } };
        Harness h = Start(handler);
        await AddAsync(h, ct, OutboxSeed.Message("1"), OutboxSeed.Message("bad"), OutboxSeed.Message("3"));

        Assert.Equal(2, await h.Dispatcher.DispatchOnceAsync(ct));

        OutboxMessage failed = Assert.Single(await OutboxAsync(h, ct));
        Assert.Equal(("bad", (short)1), (failed.EntityId, failed.Attempts));
        Assert.Equal("RecordingHandler: InvalidOperationException", failed.LastError);
        Assert.Equal(Now + OutboxDispatcher.Backoff(1), failed.NextAttemptAt);
        Assert.Equal(new[] { "1", "3" }, handler.Received.Select(c => c.EntityId));

        // Not due yet: left alone.
        Assert.Equal(0, await h.Dispatcher.DispatchOnceAsync(ct));
        Assert.Equal((short)1, Assert.Single(await OutboxAsync(h, ct)).Attempts);

        // Due, and the handler recovered: delivered and deleted.
        handler.FailFor.Clear();
        h.Time.Now = Now + OutboxDispatcher.Backoff(1);
        Assert.Equal(1, await h.Dispatcher.DispatchOnceAsync(ct));
        Assert.Empty(await OutboxAsync(h, ct));
        Assert.Equal(new[] { "1", "3", "bad" }, handler.Received.Select(c => c.EntityId));
    }

    [Fact]
    public async Task A_row_that_ran_out_of_attempts_stays_for_an_operator()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        RecordingHandler handler = new(_ => true);
        Harness h = Start(handler);
        OutboxMessage dead = OutboxSeed.Message("dead");
        dead.Attempts = OutboxDispatcher.MaxAttempts;
        await AddAsync(h, ct, dead);

        Assert.Equal(0, await h.Dispatcher.DispatchOnceAsync(ct));

        Assert.Empty(handler.Received);
        Assert.Single(await OutboxAsync(h, ct));
    }

    [Fact]
    public void The_backoff_doubles_and_stops_at_an_hour()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), OutboxDispatcher.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(10), OutboxDispatcher.Backoff(2));
        Assert.Equal(TimeSpan.FromSeconds(40), OutboxDispatcher.Backoff(4));
        Assert.Equal(TimeSpan.FromHours(1), OutboxDispatcher.Backoff(OutboxDispatcher.MaxAttempts + 5));
    }
}
