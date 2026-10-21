using DeployCraft.Server.Deployment;

namespace DeployCraft.Tests;

public class SlotQueueTests
{
    [Fact]
    public async Task PrioritizedWaiter_IsServedBeforeEarlierNormalWaiter()
    {
        var slots = new SlotQueue(1);
        await slots.WaitAsync(prioritized: false, CancellationToken.None);

        var normal = slots.WaitAsync(prioritized: false, CancellationToken.None);
        var priority = slots.WaitAsync(prioritized: true, CancellationToken.None);
        slots.Release();

        await priority.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(normal.IsCompleted);

        slots.Release();
        await normal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledWaiter_LeavesTheQueueWithoutTakingASlot()
    {
        var slots = new SlotQueue(1);
        await slots.WaitAsync(prioritized: false, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var cancelled = slots.WaitAsync(prioritized: false, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        slots.Release();
        // The returned slot is free again: a newcomer gets through right away.
        await slots.WaitAsync(prioritized: false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Resize_Up_ServesWaitingTargetsAtOnce()
    {
        var slots = new SlotQueue(1);
        await slots.WaitAsync(prioritized: false, CancellationToken.None);
        var second = slots.WaitAsync(prioritized: false, CancellationToken.None);
        var third = slots.WaitAsync(prioritized: false, CancellationToken.None);
        Assert.False(second.IsCompleted);

        slots.Resize(3);

        await Task.WhenAll(second, third).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Resize_Down_KeepsRunningTargets_AndServesOnlyUnderTheNewLimit()
    {
        var slots = new SlotQueue(2);
        await slots.WaitAsync(prioritized: false, CancellationToken.None);
        await slots.WaitAsync(prioritized: false, CancellationToken.None);
        var waiting = slots.WaitAsync(prioritized: false, CancellationToken.None);

        slots.Resize(1);
        slots.Release();
        // One computer still running for a single slot: the one waiting stays waiting.
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);

        slots.Release();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
