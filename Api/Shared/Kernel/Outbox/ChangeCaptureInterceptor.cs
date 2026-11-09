using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kernel.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kernel.Outbox;

/// <summary>
/// Writes one outbox row per insert, update and delete of an <see cref="ITrackedEntity"/> into the same save, so the
/// change and its record commit or roll back together (the transactional outbox). Also stamps events staged with
/// <see cref="OutboxEventPublisher"/> with the actor, trace and feature event, and wakes the dispatcher once the rows
/// are committed. Registered on each producing module's context (<see cref="OutboxRegistration.AddChangeCapture"/>).
/// </summary>
/// <remarks>
/// Only what the change tracker sees is captured. A row the database deletes by <c>ON DELETE CASCADE</c>, or one written
/// with raw SQL or <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, never passes through here; a feature that does that and
/// matters to a consumer publishes an event instead (e.g. <c>files_released</c>).
/// </remarks>
public sealed class ChangeCaptureInterceptor(ICurrentActor currentActor, IOutboxSignal signal, TimeProvider time)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private const string VersionProperty = "Version";

    // The saves that produced outbox rows, so the commit wakes the dispatcher only when there is new work: not for a
    // save of untracked entities, nor for the dispatcher's own delete of delivered rows.
    private readonly ConditionalWeakTable<DbContext, object> _produced = new();
    private static readonly object Marker = new();

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyIfCommitted(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        NotifyIfCommitted(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    // Inside an explicit transaction the rows are visible to the dispatcher only once it commits.

    /// <inheritdoc />
    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Notify(eventData.Context);

    /// <inheritdoc />
    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Notify(eventData.Context);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Forget(eventData.Context);

    /// <inheritdoc />
    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
            return;

        Stamp stamp = new(time.GetUtcNow(), currentActor.Current, Activity.Current?.TraceId.ToHexString(),
            EventScope.Current ?? EventScope.Unnamed);

        List<OutboxMessage> messages = context.ChangeTracker.Entries<ITrackedEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => stamp.Apply(ToMessage(e)))
            .ToList();

        bool staged = false;
        foreach (EntityEntry<OutboxMessage> entry in context.ChangeTracker.Entries<OutboxMessage>())
        {
            if (entry.State != EntityState.Added)
                continue;
            stamp.Complete(entry.Entity);
            staged = true;
        }

        if (messages.Count > 0)
            context.Set<OutboxMessage>().AddRange(messages);

        if (messages.Count > 0 || staged)
            _produced.AddOrUpdate(context, Marker);
    }

    private void NotifyIfCommitted(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null)
            Notify(context);
    }

    private void Notify(DbContext? context)
    {
        if (context is not null && _produced.Remove(context))
            signal.Notify();
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
            _produced.Remove(context);
    }

    private static OutboxMessage ToMessage(EntityEntry entry)
    {
        ChangeAction action = entry.State switch
        {
            EntityState.Added => ChangeAction.Insert,
            EntityState.Deleted => ChangeAction.Delete,
            _ => ChangeAction.Update,
        };

        return new OutboxMessage
        {
            Id = OutboxIds.Next(),
            Kind = OutboxKind.RowChange,
            Module = entry.Metadata.GetSchema() ?? string.Empty,
            EntityType = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name,
            EntityId = string.Join(':', entry.Metadata.FindPrimaryKey()?.Properties
                .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty) ?? []),
            Action = action,
            OldValue = action == ChangeAction.Insert ? null : Serialize(entry.OriginalValues),
            NewValue = action == ChangeAction.Delete ? null : Serialize(entry.CurrentValues),
            Version = VersionOf(entry, action),
        };
    }

    /// <summary>
    /// The version the row has after the change. The database sets it (default 1, then the audit trigger's +1) after this
    /// runs, so it is computed the way the database will, not read from the not-yet-updated entity.
    /// </summary>
    private static short VersionOf(EntityEntry entry, ChangeAction action)
    {
        if (entry.Metadata.FindProperty(VersionProperty) is null)
            return 0;

        short original = entry.OriginalValues[VersionProperty] as short? ?? 0;
        return action switch
        {
            ChangeAction.Insert => 1,
            ChangeAction.Update => (short)(original + 1),
            _ => original,
        };
    }

    // Keyed by CLR property name, the names consumers register (e.g. FileColumn.Property).
    private static string Serialize(PropertyValues values)
    {
        Dictionary<string, object?> snapshot = new(values.Properties.Count);
        foreach (IProperty property in values.Properties)
            snapshot[property.Name] = values[property];
        return JsonSerializer.Serialize(snapshot);
    }

    private sealed record Stamp(DateTimeOffset OccurredAt, Actor Actor, string? TraceId, string Event)
    {
        public OutboxMessage Apply(OutboxMessage message)
        {
            message.Event = Event;
            message.ActorType = Actor.Type;
            message.ActorUserId = Actor.UserId;
            message.ActorService = Actor.Service;
            message.TraceId = TraceId;
            message.OccurredAt = OccurredAt;
            return message;
        }

        /// <summary>Fills what a staged event left out; a published event keeps its own name.</summary>
        public void Complete(OutboxMessage message)
        {
            if (message.ActorType is null)
            {
                message.ActorType = Actor.Type;
                message.ActorUserId = Actor.UserId;
                message.ActorService = Actor.Service;
            }

            message.Event ??= Event;
            message.TraceId ??= TraceId;
            if (message.OccurredAt == default)
                message.OccurredAt = OccurredAt;
        }
    }
}
