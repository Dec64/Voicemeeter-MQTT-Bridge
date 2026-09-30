using System.Threading.Channels;

namespace VoicemeeterMqttBridge.Tests;

public sealed class LatestMeterSnapshotQueueTests
{
    private static MeterWindowSnapshot Snapshot(int milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds),
        Array.AsReadOnly(Array.Empty<MeterWindowReading>()));

    [Fact]
    public async Task Stalled_reader_keeps_only_latest_frame_without_blocking_producer()
    {
        var queue = new LatestMeterSnapshotQueue();
        for (int i = 1; i <= 10000; i++) Assert.True(queue.TryWrite(Snapshot(i)));
        Assert.Equal(TimeSpan.FromMilliseconds(10000), (await queue.ReadAsync()).Duration);
        Task<MeterWindowSnapshot> next = queue.ReadAsync().AsTask();
        Assert.False(next.IsCompleted);
        var fresh = Snapshot(50);
        Assert.True(queue.TryWrite(fresh));
        Assert.Same(fresh, await next.WaitAsync(TimeSpan.FromSeconds(5)));
        queue.Complete();
    }

    [Fact]
    public async Task Completion_rejects_new_frames_and_drains_the_last_pending_one()
    {
        var queue = new LatestMeterSnapshotQueue();
        var latest = Snapshot(50);
        queue.TryWrite(Snapshot(25)); queue.TryWrite(latest);
        queue.Complete(); queue.Complete();
        Assert.False(queue.TryWrite(Snapshot(75)));
        Assert.Same(latest, await queue.ReadAsync());
        await Assert.ThrowsAsync<ChannelClosedException>(() => queue.ReadAsync().AsTask());
    }

    [Fact]
    public async Task Cancelled_read_does_not_close_queue_or_consume_later_frames()
    {
        var queue = new LatestMeterSnapshotQueue();
        using var cancellation = new CancellationTokenSource();
        Task<MeterWindowSnapshot> read = queue.ReadAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        var latest = Snapshot(50);
        Assert.True(queue.TryWrite(latest));
        Assert.Same(latest, await queue.ReadAsync());
        queue.Complete();
    }

    [Fact]
    public async Task Concurrent_writers_leave_the_last_submitted_snapshot()
    {
        var queue = new LatestMeterSnapshotQueue();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 1; i <= 1000; i++) Assert.True(queue.TryWrite(Snapshot(i)));
        }))).WaitAsync(TimeSpan.FromSeconds(5));
        var final = Snapshot(50);
        Assert.True(queue.TryWrite(final));
        Assert.Same(final, await queue.ReadAsync());
        queue.Complete();
    }

    [Fact]
    public async Task Completing_empty_queue_wakes_waiting_reader()
    {
        var queue = new LatestMeterSnapshotQueue();
        Task<MeterWindowSnapshot> read = queue.ReadAsync().AsTask();
        queue.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentNullException>(() => queue.TryWrite(null!));
    }
}
