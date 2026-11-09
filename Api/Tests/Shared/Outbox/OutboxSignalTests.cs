using Kernel.Outbox;

namespace Tests.Shared.Outbox;

/// <summary>The wake signal holds one pending wake, however many notifications arrive.</summary>
public class OutboxSignalTests
{
    [Fact]
    public async Task Several_notifications_wake_one_wait_and_a_second_wait_times_out()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using OutboxSignal signal = new();

        signal.Notify();
        signal.Notify();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), ct));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), ct));
    }
}
