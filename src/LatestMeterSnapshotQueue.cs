// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Threading.Channels;
using System.Text.Json.Serialization;

namespace VoicemeeterMqttBridge;

public sealed record MeterQueueDiagnostics(
    [property: JsonPropertyName("depth")] int Depth,
    [property: JsonPropertyName("coalesced_count")] long CoalescedCount,
    [property: JsonPropertyName("stale_drop_count")] long StaleDropCount);

/// <summary>
/// One pending snapshot per cadence. Serialize after reading, with the send timestamp.
/// Superseded telemetry is discarded;
/// commands/discovery must never use this lossy queue. No MQTT connection is opened.
/// </summary>
public sealed class LatestMeterSnapshotQueue
{
    private long _coalesced, _stale;
    private readonly Channel<MeterWindowSnapshot> _snapshots;

    public LatestMeterSnapshotQueue()
    {
        _snapshots = Channel.CreateBounded<MeterWindowSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        }, _ => Interlocked.Increment(ref _coalesced));
    }

    public MeterQueueDiagnostics GetDiagnostics() => new(_snapshots.Reader.Count,
        Interlocked.Read(ref _coalesced), Interlocked.Read(ref _stale));

    internal void RecordStaleDrop() => Interlocked.Increment(ref _stale);

    public bool TryWrite(MeterWindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _snapshots.Writer.TryWrite(snapshot);
    }

    public ValueTask<MeterWindowSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        => _snapshots.Reader.ReadAsync(cancellationToken);

    /// <summary>
    /// Discard expired/untracked snapshots and wait for fresh data. Budget must exceed
    /// the intended measurement window. Cancellation and completion retain channel semantics.
    /// </summary>
    public async ValueTask<MeterWindowSnapshot> ReadFreshAsync(TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        if (maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumAge));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await _snapshots.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.IsFresh(maximumAge)) return snapshot;
            RecordStaleDrop();
        }
    }

    public void Complete() => _snapshots.Writer.TryComplete();
}
