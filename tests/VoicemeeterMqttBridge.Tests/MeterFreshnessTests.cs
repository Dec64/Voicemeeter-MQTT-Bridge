using System.Threading.Channels;

namespace VoicemeeterMqttBridge.Tests;

public sealed class MeterFreshnessTests
{
    private readonly ManualTimeProvider _time = new();
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(100);

    private MeterWindowSnapshot Capture(int duration = 50)
    {
        var settings = new MeteringV2Settings
        {
            Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } } }
        };
        var window = new MeterWindowAccumulator(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings, _time);
        window.Observe(new("strip:0", MeterTap.PreFader, true, 0.5f, null));
        _time.Advance(TimeSpan.FromMilliseconds(duration));
        return window.CompleteWindow();
    }

    [Fact]
    public void Freshness_includes_measurement_window_and_expires_at_exact_budget()
    {
        var snapshot = Capture();
        Assert.True(snapshot.IsFresh(Budget));
        _time.Advance(TimeSpan.FromMilliseconds(49));
        Assert.True(snapshot.IsFresh(Budget));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(snapshot.IsFresh(Budget));
        Assert.False(Capture(100).IsFresh(Budget));
    }

    [Fact]
    public async Task Late_enqueue_does_not_refresh_old_data_and_reader_recovers()
    {
        var queue = new LatestMeterSnapshotQueue();
        var stale = Capture();
        _time.Advance(Budget);
        queue.TryWrite(stale);
        var read = queue.ReadFreshAsync(Budget).AsTask();
        Assert.False(read.IsCompleted);
        var fresh = Capture();
        queue.TryWrite(fresh);
        Assert.Same(fresh, await read.WaitAsync(TimeSpan.FromSeconds(5)));
        // A caller that stalls after dequeue must recheck immediately before sending.
        _time.Advance(Budget);
        Assert.False(fresh.IsFresh(Budget));
        queue.Complete();
    }

    [Fact]
    public async Task Pending_frame_can_expire_after_enqueue()
    {
        var queue = new LatestMeterSnapshotQueue();
        queue.TryWrite(Capture());
        _time.Advance(Budget);
        queue.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => queue.ReadFreshAsync(Budget).AsTask());
    }

    [Fact]
    public async Task Unknown_capture_age_is_not_treated_as_fresh()
    {
        var snapshot = new MeterWindowSnapshot(TimeSpan.FromMilliseconds(50), Array.Empty<MeterWindowReading>());
        Assert.False(snapshot.IsFresh(Budget));
        var queue = new LatestMeterSnapshotQueue();
        queue.TryWrite(snapshot);
        queue.Complete();
        await Assert.ThrowsAsync<ChannelClosedException>(() => queue.ReadFreshAsync(Budget).AsTask());
    }

    [Fact]
    public async Task Cancellation_after_discarding_stale_data_leaves_queue_usable()
    {
        var queue = new LatestMeterSnapshotQueue();
        queue.TryWrite(Capture(100));
        using var cancellation = new CancellationTokenSource();
        var read = queue.ReadFreshAsync(Budget, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        var fresh = Capture();
        queue.TryWrite(fresh);
        Assert.Same(fresh, await queue.ReadFreshAsync(Budget));
        queue.Complete();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_budget_is_rejected_even_when_queue_is_empty(int milliseconds)
    {
        var budget = TimeSpan.FromMilliseconds(milliseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => Capture().IsFresh(budget));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new LatestMeterSnapshotQueue().ReadFreshAsync(budget).AsTask());
    }

    [Fact]
    public async Task Each_window_gets_its_own_origin_and_fresh_unavailable_is_deliverable()
    {
        var settings = new MeteringV2Settings
        {
            Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } } }
        };
        var window = new MeterWindowAccumulator(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings, _time);
        window.Observe(new("strip:0", MeterTap.PreFader, true, 0, null));
        _time.Advance(Budget);
        Assert.False(window.CompleteWindow().IsFresh(Budget));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        var unavailable = window.CompleteWindow();
        Assert.True(unavailable.IsFresh(Budget));
        Assert.False(Assert.Single(unavailable.Readings).Peak.Available);
        var queue = new LatestMeterSnapshotQueue();
        queue.TryWrite(unavailable);
        Assert.Same(unavailable, await queue.ReadFreshAsync(Budget));
        queue.Complete();
    }

    [Fact]
    public void Clock_moving_before_window_start_cannot_make_snapshot_fresh()
    {
        var snapshot = Capture();
        _time.Advance(TimeSpan.FromMilliseconds(-51));
        Assert.False(snapshot.IsFresh(Budget));
    }
}
