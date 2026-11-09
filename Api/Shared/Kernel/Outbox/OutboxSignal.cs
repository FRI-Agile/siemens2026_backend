namespace Kernel.Outbox;

/// <summary>
/// Wakes the dispatcher when a save committed outbox rows, so delivery follows a change within milliseconds instead of
/// at the next poll.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>Records that there is work; several notifications before a wait count as one.</summary>
    void Notify();

    /// <summary>Waits for a notification or the timeout; <c>true</c> when notified.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct = default);
}

/// <inheritdoc cref="IOutboxSignal"/>
public sealed class OutboxSignal : IOutboxSignal, IDisposable
{
    // Starts unsignalled and holds at most one wake: the dispatcher drains everything once woken.
    private readonly SemaphoreSlim _gate = new(0, 1);

    /// <inheritdoc />
    public void Notify()
    {
        try
        {
            _gate.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    /// <inheritdoc />
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct = default) => _gate.WaitAsync(timeout, ct);

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
