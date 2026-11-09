using System.Text.Json;

namespace Kernel.Outbox;

/// <summary>Publishes a domain event on a module's own outbox.</summary>
public static class OutboxEventPublisher
{
    /// <summary>
    /// Stages an event on <paramref name="outbox"/>, committed by its next save together with the change that justified
    /// it. The change-capture interceptor stamps the actor, the trace and the ambient feature event onto it.
    /// </summary>
    /// <param name="outbox">The producing module's context.</param>
    /// <param name="module">The producing module's name (its schema).</param>
    /// <param name="eventName">The event, lower snake_case, no module prefix (e.g. <c>files_released</c>).</param>
    /// <param name="entityType">What the event is about.</param>
    /// <param name="entityId">Which one.</param>
    /// <param name="payload">Serialized to JSON as the event's <c>new_value</c>.</param>
    public static void PublishEvent<TPayload>(
        this IOutboxDbContext outbox, string module, string eventName, string entityType, string entityId, TPayload payload)
    {
        outbox.OutboxMessages.Add(new OutboxMessage
        {
            Id = OutboxIds.Next(),
            Kind = OutboxKind.DomainEvent,
            Module = module,
            Event = eventName,
            EntityType = entityType,
            EntityId = entityId,
            Action = ChangeAction.Insert, // an event "occurred"; the row-change value set is reused as it is
            NewValue = JsonSerializer.Serialize(payload),
        });
    }
}
