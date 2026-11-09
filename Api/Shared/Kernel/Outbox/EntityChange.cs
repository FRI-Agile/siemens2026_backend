namespace Kernel.Outbox;

/// <summary>What happened to a row (<c>outbox_message.action</c>).</summary>
public enum ChangeAction
{
    /// <summary>The row was inserted, or an event occurred.</summary>
    Insert,

    /// <summary>The row was updated.</summary>
    Update,

    /// <summary>The row was deleted.</summary>
    Delete,
}

/// <summary>What an outbox row carries (<c>outbox_message.kind</c>).</summary>
public enum OutboxKind
{
    /// <summary>A captured insert, update or delete of an <see cref="ITrackedEntity"/>.</summary>
    RowChange,

    /// <summary>An event a module published for others (<see cref="OutboxEventPublisher"/>).</summary>
    DomainEvent,

    /// <summary>An application audit event bound for the audit log. Reserved: nothing writes one yet.</summary>
    AuditEvent,
}

/// <summary>The stored codes of <see cref="OutboxKind"/> and <see cref="ChangeAction"/>.</summary>
public static class OutboxCodes
{
    /// <summary>The stored code of <paramref name="kind"/>.</summary>
    public static string Of(OutboxKind kind) => kind switch
    {
        OutboxKind.RowChange => "row_change",
        OutboxKind.DomainEvent => "domain_event",
        OutboxKind.AuditEvent => "audit_event",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The <see cref="OutboxKind"/> stored as <paramref name="code"/>.</summary>
    public static OutboxKind KindOf(string code) => code switch
    {
        "row_change" => OutboxKind.RowChange,
        "domain_event" => OutboxKind.DomainEvent,
        "audit_event" => OutboxKind.AuditEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    /// <summary>The stored code of <paramref name="action"/>.</summary>
    public static string Of(ChangeAction action) => action switch
    {
        ChangeAction.Insert => "insert",
        ChangeAction.Update => "update",
        ChangeAction.Delete => "delete",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>The <see cref="ChangeAction"/> stored as <paramref name="code"/>.</summary>
    public static ChangeAction ActionOf(string code) => code switch
    {
        "insert" => ChangeAction.Insert,
        "update" => ChangeAction.Update,
        "delete" => ChangeAction.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}

/// <summary>
/// Who caused a change, captured when it was written, since the dispatcher runs later with no request to ask
/// (<c>outbox_message.actor_*</c>).
/// </summary>
/// <param name="Type">One of <see cref="ActorTypes"/>.</param>
/// <param name="UserId">The staff member's Entra object id; set exactly for <see cref="ActorTypes.User"/>.</param>
/// <param name="Service">The acting service's name; set exactly for <see cref="ActorTypes.Service"/>.</param>
public sealed record Actor(string Type, Guid? UserId = null, string? Service = null)
{
    /// <summary>The API itself.</summary>
    public static Actor System { get; } = new(ActorTypes.System);

    /// <summary>A caller without a staff token.</summary>
    public static Actor Anonymous { get; } = new(ActorTypes.Anonymous);

    /// <summary>A signed-in staff member.</summary>
    public static Actor ForUser(Guid userId) => new(ActorTypes.User, userId);
}

/// <summary>The value set of <see cref="Actor.Type"/>.</summary>
public static class ActorTypes
{
    /// <summary>A signed-in staff member.</summary>
    public const string User = "user";

    /// <summary>Another service calling the API. Reserved: nothing calls it yet.</summary>
    public const string Service = "service";

    /// <summary>The API itself: a scheduled worker, the outbox dispatcher, startup.</summary>
    public const string System = "system";

    /// <summary>A request without a staff token: an applicant.</summary>
    public const string Anonymous = "anonymous";

    /// <summary>A request whose caller could not be determined.</summary>
    public const string Unknown = "unknown";
}

/// <summary>Who is acting right now, for the change-capture interceptor. The host implements it from the request.</summary>
public interface ICurrentActor
{
    /// <summary>The actor of the current request, or <see cref="Actor.System"/> outside one.</summary>
    Actor Current { get; }
}

/// <summary>One outbox row as consumers see it.</summary>
/// <param name="Id">The row's id, also the change's identity: consumers are idempotent on it.</param>
/// <param name="Kind">A row change or a published event.</param>
/// <param name="Module">The producing module (its schema).</param>
/// <param name="Event">The feature event that caused it, or a published event's name.</param>
/// <param name="EntityType">The table the change is about.</param>
/// <param name="EntityId">That row's key as text.</param>
/// <param name="Action">Insert, update or delete; <see cref="ChangeAction.Insert"/> for an event.</param>
/// <param name="OldValue">The row before the change, as JSON keyed by CLR property name; <c>null</c> on insert.</param>
/// <param name="NewValue">The row after it, or an event's payload; <c>null</c> on delete.</param>
/// <param name="Actor">Who caused it.</param>
/// <param name="TraceId">The W3C trace id active when it was written.</param>
/// <param name="OccurredAt">When it was written.</param>
public sealed record EntityChange(
    Guid Id,
    OutboxKind Kind,
    string Module,
    string Event,
    string EntityType,
    string EntityId,
    ChangeAction Action,
    string? OldValue,
    string? NewValue,
    Actor Actor,
    string? TraceId,
    DateTimeOffset OccurredAt);
