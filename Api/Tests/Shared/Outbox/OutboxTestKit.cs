using Kernel.Outbox;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using Tests.RecruitmentModule.Features.JobPostFeatures;
using Tests.RecruitmentModule.Infrastructure;

namespace Tests.Shared.Outbox;

/// <summary>An <see cref="ICurrentActor"/> fixed to one actor.</summary>
internal sealed class StubActor(Actor actor) : ICurrentActor
{
    public Actor Current { get; set; } = actor;
}

/// <summary>An <see cref="IOutboxSignal"/> that counts notifications and never waits.</summary>
internal sealed class CountingSignal : IOutboxSignal
{
    public int Notified { get; private set; }

    public void Notify() => Notified++;

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct = default) => Task.FromResult(false);
}

/// <summary>A handler that records what it is given, and fails for the entity ids in <see cref="FailFor"/>.</summary>
internal sealed class RecordingHandler(Func<EntityChange, bool> matches) : IEntityChangeHandler
{
    public List<EntityChange> Received { get; } = [];

    public HashSet<string> FailFor { get; } = [];

    public bool Matches(EntityChange change) => matches(change);

    public Task HandleAsync(IReadOnlyList<EntityChange> changes, CancellationToken ct = default)
    {
        if (changes.Any(c => FailFor.Contains(c.EntityId)))
            throw new InvalidOperationException("The handler failed.");
        Received.AddRange(changes);
        return Task.CompletedTask;
    }
}

/// <summary>Seeds the parent rows an <c>application_document</c> needs.</summary>
internal static class OutboxSeed
{
    /// <summary>A stored application to attach documents to.</summary>
    public static async Task<Application> ApplicationAsync(RecruitmentDbContext db, CancellationToken ct)
    {
        JobPost post = JobPostSeed.Post("Elektrotechnik údržby");
        await JobPostSeed.AddAsync(db, ct, post);
        Application application = CandidateSeed.Application(
            post, CandidateSeed.Candidate("Martin", "Kašuba", $"kasuba{Guid.NewGuid():N}@example.com"));
        db.Applications.Add(application);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return application;
    }

    /// <summary>An outbox row ready to insert as it is, bypassing capture.</summary>
    public static OutboxMessage Message(string entityId, string entityType = "application_document") => new()
    {
        Id = OutboxIds.Next(),
        Kind = OutboxKind.RowChange,
        Module = "recruitment",
        Event = "test_event",
        EntityType = entityType,
        EntityId = entityId,
        Action = ChangeAction.Insert,
        NewValue = "{}",
        ActorType = ActorTypes.System,
        OccurredAt = DateTimeOffset.UtcNow,
    };
}
