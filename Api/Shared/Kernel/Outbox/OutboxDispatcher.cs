using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kernel.Outbox;

/// <summary>
/// Drains every module's outbox and delivers the rows to the registered <see cref="IEntityChangeHandler"/>s, oldest
/// first, deleting each row once every handler that wants it took it (conventions.md § Outbox pattern). Woken by the
/// <see cref="IOutboxSignal"/> right after a save committed rows, and every <see cref="PollInterval"/> regardless.
/// </summary>
/// <remarks>
/// <para>
/// <b>At-least-once.</b> A crash between delivery and the delete delivers the rows again, so every handler is
/// idempotent. One dispatcher runs per API process; a second process would at worst deliver a row twice.
/// </para>
/// <para>
/// <b>A failing row does not hold up the rest.</b> When a batch fails, its rows are delivered one by one, so the rows
/// that go through are deleted and only the failing ones stay, each with its attempt count, the failure's handler and
/// exception type, and the time it may be tried again (an exponential backoff up to an hour). After
/// <see cref="MaxAttempts"/> a row is left for an operator and no longer picked up; that logs an error. A later row may
/// therefore be delivered before an earlier one that failed.
/// </para>
/// </remarks>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory, IOutboxSignal signal, TimeProvider time, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    /// <summary>Rows taken from one outbox per pass.</summary>
    internal const int BatchSize = 100;

    /// <summary>Failed deliveries after which a row is left for an operator.</summary>
    public const short MaxAttempts = 10;

    /// <summary>How long the dispatcher waits for a wake before looking anyway: a missed signal, a row due for retry.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int delivered;
            try
            {
                delivered = await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The outbox itself could not be read or written, e.g. the database is down; rows stay for the next pass.
                LogPassFailed(logger, ex);
                delivered = 0;
            }

            // A full batch means there is probably more: drain on without waiting.
            if (delivered < BatchSize)
            {
                try
                {
                    await signal.WaitAsync(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>One pass over every module's outbox; returns the most rows any one outbox delivered.</summary>
    internal async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        List<IEntityChangeHandler> handlers = [.. scope.ServiceProvider.GetServices<IEntityChangeHandler>()];

        int most = 0;
        foreach (IOutboxDbContext outbox in scope.ServiceProvider.GetServices<IOutboxDbContext>())
            most = Math.Max(most, await DrainAsync(outbox, handlers, ct));
        return most;
    }

    private async Task<int> DrainAsync(IOutboxDbContext outbox, List<IEntityChangeHandler> handlers, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        List<OutboxMessage> due = await outbox.OutboxMessages
            .Where(m => m.Attempts < MaxAttempts && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
            return 0;

        // The usual case: the whole batch goes through, and is deleted in one save.
        if (await DeliverAsync(due, handlers, ct) is null)
        {
            outbox.OutboxMessages.RemoveRange(due);
            await outbox.SaveChangesAsync(ct);
            return due.Count;
        }

        // Something failed: find which rows, so the others are not held back by them.
        int delivered = 0;
        foreach (OutboxMessage message in due)
        {
            if (await DeliverAsync([message], handlers, ct) is not { } failure)
            {
                outbox.OutboxMessages.Remove(message);
                delivered++;
                continue;
            }

            message.Attempts++;
            message.LastError = failure;
            message.NextAttemptAt = time.GetUtcNow() + Backoff(message.Attempts);
            if (message.Attempts >= MaxAttempts)
                LogGaveUp(logger, message.Module, message.EntityType, failure, message.Attempts);
        }

        await outbox.SaveChangesAsync(ct);
        return delivered;
    }

    /// <summary>
    /// Hands each handler the changes it wants. Returns <c>null</c> when all succeeded, else the first failure as
    /// <c>Handler: ExceptionType</c>; the other handlers still run, so a failure in one does not starve the rest.
    /// </summary>
    private async Task<string?> DeliverAsync(
        IReadOnlyList<OutboxMessage> messages, List<IEntityChangeHandler> handlers, CancellationToken ct)
    {
        List<EntityChange> changes = messages.Select(ToChange).ToList();
        string? failure = null;

        foreach (IEntityChangeHandler handler in handlers)
        {
            List<EntityChange> matched = changes.Where(handler.Matches).ToList();
            if (matched.Count == 0)
                continue;

            string name = handler.GetType().Name;
            try
            {
                // Names the handler on whatever its own work logs: dispatch has no request to correlate with.
                using (logger.BeginScope(new Dictionary<string, object?> { ["Handler"] = name }))
                    await handler.HandleAsync(matched, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                LogHandlerFailed(logger, name, ex);
                failure ??= $"{name}: {ex.GetType().Name}";
            }
        }

        return failure;
    }

    /// <summary>5 s, 10 s, 20 s, … doubling per attempt, at most an hour.</summary>
    internal static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 5 * Math.Pow(2, attempts - 1)));

    internal static EntityChange ToChange(OutboxMessage m) => new(
        m.Id, m.Kind, m.Module, m.Event ?? Events.EventScope.Unnamed, m.EntityType, m.EntityId, m.Action,
        m.OldValue, m.NewValue, new Actor(m.ActorType ?? ActorTypes.Unknown, m.ActorUserId, m.ActorService),
        m.TraceId, m.OccurredAt);

    [LoggerMessage(EventId = 2000, EventName = "outbox.pass.failed", Level = LogLevel.Error,
        Message = "Outbox dispatch pass failed; the rows stay for the next one.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2001, EventName = "outbox.handler.failed", Level = LogLevel.Warning,
        Message = "Outbox handler {Handler} failed; its changes will be delivered again.")]
    private static partial void LogHandlerFailed(ILogger logger, string handler, Exception exception);

    // No entity id: a record identifier stays out of operational logs (ADR-A009). The row itself is in the outbox table.
    [LoggerMessage(EventId = 2002, EventName = "outbox.message.gave_up", Level = LogLevel.Error,
        Message = "Outbox gave up on a {Module}.{EntityType} change after {Attempts} attempts ({Failure}); it stays in the outbox for an operator.")]
    private static partial void LogGaveUp(ILogger logger, string module, string entityType, string failure, int attempts);
}
