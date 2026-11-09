namespace Kernel.Outbox;

/// <summary>
/// A module's reaction to changes in other modules, delivered by the <see cref="OutboxDispatcher"/>. Implementations
/// live in the consuming module's <c>Infrastructure/Messaging/</c> and are registered scoped
/// (<see cref="OutboxRegistration.AddEntityChangeHandlers"/>).
/// </summary>
/// <remarks>
/// Delivery is <b>at-least-once</b>: a batch is delivered again after a failure or a crash before its rows were
/// deleted, possibly to a handler that already took it. Every handler must be idempotent, and match on the contract
/// strings (module, entity type, event), never on another module's types.
/// </remarks>
public interface IEntityChangeHandler
{
    /// <summary>Whether this handler wants <paramref name="change"/>.</summary>
    bool Matches(EntityChange change);

    /// <summary>Handles the matching changes of one batch, in the order they occurred.</summary>
    /// <exception cref="Exception">Any failure: the changes are delivered again later.</exception>
    Task HandleAsync(IReadOnlyList<EntityChange> changes, CancellationToken ct = default);
}
