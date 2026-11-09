using Microsoft.EntityFrameworkCore;

namespace Kernel.Outbox;

/// <summary>
/// One undelivered row of a module's outbox (<c>&lt;module&gt;.outbox_message</c>, conventions.md § Outbox pattern):
/// written in the same transaction as the change it describes, deleted once every consumer took it.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>The row's id (v7, so time-ordered): the dispatch order, and the change's identity for idempotent consumers.</summary>
    public Guid Id { get; set; }

    /// <summary>A captured row change or a published event.</summary>
    public OutboxKind Kind { get; set; }

    /// <summary>The producing module, which is its schema.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>The feature event; <c>null</c> until the interceptor stamps a staged event with the ambient one.</summary>
    public string? Event { get; set; }

    /// <summary>The table the change is about, or what an event is about.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>That row's key as text, composite keys joined with <c>:</c>.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Insert, update or delete; insert for an event.</summary>
    public ChangeAction Action { get; set; }

    /// <summary>The row before the change, JSON keyed by CLR property name; <c>null</c> on insert and for events.</summary>
    public string? OldValue { get; set; }

    /// <summary>The row after the change, or an event's payload; <c>null</c> on delete.</summary>
    public string? NewValue { get; set; }

    /// <summary>One of <see cref="ActorTypes"/>; <c>null</c> until the interceptor stamps a staged event.</summary>
    public string? ActorType { get; set; }

    /// <summary>The staff member's Entra object id; set exactly for a <see cref="ActorTypes.User"/>.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The acting service; set exactly for a <see cref="ActorTypes.Service"/>.</summary>
    public string? ActorService { get; set; }

    /// <summary>The W3C trace id active when the row was written.</summary>
    public string? TraceId { get; set; }

    /// <summary>When the change was saved.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The entity's optimistic-locking version at the change, or 0 for an entity without one.</summary>
    public short Version { get; set; }

    /// <summary>Failed deliveries so far; the dispatcher gives up at <see cref="OutboxDispatcher.MaxAttempts"/>.</summary>
    public short Attempts { get; set; }

    /// <summary>When a failed row may be tried again; <c>null</c> for a row never tried.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>The handler and exception type of the last failure, never its message, which may carry personal data.</summary>
    public string? LastError { get; set; }
}

/// <summary>A module context that holds an outbox: what the dispatcher drains.</summary>
public interface IOutboxDbContext
{
    /// <summary>The module's undelivered rows.</summary>
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>Saves the context, as <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Maps <see cref="OutboxMessage"/> in a module's context.</summary>
public static class OutboxModelExtensions
{
    /// <summary>Maps <c>outbox_message</c> in the context's default schema; the DDL script owns the table (ADR-D002).</summary>
    /// <param name="modelBuilder">The module context's model builder.</param>
    /// <returns>The same <paramref name="modelBuilder"/> for chaining.</returns>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_message", t => t.ExcludeFromMigrations());
            b.HasKey(m => m.Id);

            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.Kind).HasColumnName("kind")
                .HasConversion(v => OutboxCodes.Of(v), s => OutboxCodes.KindOf(s));
            b.Property(m => m.Module).HasColumnName("module");
            b.Property(m => m.Event).HasColumnName("event").IsRequired();
            b.Property(m => m.EntityType).HasColumnName("entity_type");
            b.Property(m => m.EntityId).HasColumnName("entity_id");
            b.Property(m => m.Action).HasColumnName("action")
                .HasConversion(v => OutboxCodes.Of(v), s => OutboxCodes.ActionOf(s));
            b.Property(m => m.OldValue).HasColumnName("old_value").HasColumnType("jsonb");
            b.Property(m => m.NewValue).HasColumnName("new_value").HasColumnType("jsonb");
            b.Property(m => m.ActorType).HasColumnName("actor_type").IsRequired();
            b.Property(m => m.ActorUserId).HasColumnName("actor_user_id");
            b.Property(m => m.ActorService).HasColumnName("actor_service");
            b.Property(m => m.TraceId).HasColumnName("trace_id");
            b.Property(m => m.OccurredAt).HasColumnName("occurred_at");
            b.Property(m => m.Version).HasColumnName("version");
            b.Property(m => m.Attempts).HasColumnName("attempts");
            b.Property(m => m.NextAttemptAt).HasColumnName("next_attempt_at");
            b.Property(m => m.LastError).HasColumnName("last_error");
        });

        return modelBuilder;
    }
}
