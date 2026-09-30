// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Threading.Channels;

namespace VoicemeeterMqttBridge;

/// <summary>
/// One pending snapshot per cadence. Serialize after reading, with the send timestamp.
/// Superseded telemetry is discarded;
/// commands/discovery must never use this lossy queue. No MQTT connection is opened.
/// </summary>
public sealed class LatestMeterSnapshotQueue
{
    private readonly Channel<MeterWindowSnapshot> _snapshots = Channel.CreateBounded<MeterWindowSnapshot>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    public bool TryWrite(MeterWindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _snapshots.Writer.TryWrite(snapshot);
    }

    public ValueTask<MeterWindowSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        => _snapshots.Reader.ReadAsync(cancellationToken);

    public void Complete() => _snapshots.Writer.TryComplete();
}
