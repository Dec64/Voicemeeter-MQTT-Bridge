// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Threading.Channels;
using System.Text.Json.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge;

public sealed record MeterPublishDiagnostics(
    [property: JsonPropertyName("elapsed_seconds")] double ElapsedSeconds,
    [property: JsonPropertyName("fast_publish_count")] long FastPublishCount,
    [property: JsonPropertyName("slow_publish_count")] long SlowPublishCount)
{
    [JsonPropertyName("fast_queue")] public MeterQueueDiagnostics? FastQueue { get; init; }
    [JsonPropertyName("slow_queue")] public MeterQueueDiagnostics? SlowQueue { get; init; }
    [JsonPropertyName("sampling")] public MeterSamplingDiagnostics? Sampling { get; init; }
    [JsonPropertyName("last_fast_age_at_send_ms")] public double? LastFastAgeAtSendMs { get; init; }
    [JsonPropertyName("last_slow_age_at_send_ms")] public double? LastSlowAgeAtSendMs { get; init; }
}

/// <summary>
/// Single publishing session on an already connected MQTT client. Does not connect,
/// reconnect, discover entities or start sampling. Not wired into BridgeService.
/// This instance exclusively owns frame serialization on the supplied builder.
/// </summary>
public sealed class MeterTelemetryPublisher
{
    private readonly IMqttClient _client;
    private readonly AggregateFrameBuilder _frames;
    private readonly string _baseTopic;
    private readonly TimeProvider _time;
    private readonly object _serialization = new();
    private int _started;
    private readonly object _metricsLock = new();
    private readonly long _origin;
    private long? _finished;
    private long _fastPublished;
    private long _slowPublished;
    private double? _lastFastAgeAtSendMs, _lastSlowAgeAtSendMs;
    private LatestMeterSnapshotQueue? _fastQueue, _slowQueue;
    // The session owner stops sampling immediately on failure, before sibling sends drain.
    internal Action? RequestSessionStop { get; init; }

    public MeterTelemetryPublisher(IMqttClient client, AggregateFrameBuilder frames,
        string baseTopic, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseTopic);
        if (baseTopic.IndexOfAny(['+', '#', '\0']) >= 0)
            throw new ArgumentException("A publishing base topic cannot contain wildcards or NUL.", nameof(baseTopic));
        _client = client;
        _frames = frames;
        _baseTopic = baseTopic;
        _time = timeProvider ?? TimeProvider.System;
        _origin = _time.GetTimestamp();
    }

    public MeterPublishDiagnostics GetDiagnostics()
    {
        lock (_metricsLock)
            return new(_time.GetElapsedTime(_origin, _finished ?? _time.GetTimestamp()).TotalSeconds,
                _fastPublished, _slowPublished)
            {
                FastQueue = _fastQueue?.GetDiagnostics(), SlowQueue = _slowQueue?.GetDiagnostics(),
                LastFastAgeAtSendMs = _lastFastAgeAtSendMs, LastSlowAgeAtSendMs = _lastSlowAgeAtSendMs
            };
    }

    public async Task RunAsync(LatestMeterSnapshotQueue fast, LatestMeterSnapshotQueue slow,
        MeteringV2Settings settings, TimeSpan fastMaximumAge, TimeSpan slowMaximumAge,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("A telemetry publisher can only run once.");
        ArgumentNullException.ThrowIfNull(fast);
        ArgumentNullException.ThrowIfNull(slow);
        ArgumentNullException.ThrowIfNull(settings);
        lock (_metricsLock) { _fastQueue = fast; _slowQueue = slow; }
        settings.Validate();
        if (!settings.Enabled || (!settings.FastEnabled && !settings.SlowEnabled)) return;
        if (settings.FastEnabled && settings.SlowEnabled && ReferenceEquals(fast, slow))
            throw new ArgumentException("Fast and slow streams require separate single-reader queues.", nameof(slow));
        if (settings.FastEnabled && fastMaximumAge <= TimeSpan.FromMilliseconds(settings.FastPublishIntervalMs))
            throw new ArgumentOutOfRangeException(nameof(fastMaximumAge), "The fast age budget must exceed its window.");
        if (settings.SlowEnabled && slowMaximumAge <= TimeSpan.FromMilliseconds(settings.SlowPublishIntervalMs))
            throw new ArgumentOutOfRangeException(nameof(slowMaximumAge), "The slow age budget must exceed its window.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fastTask = settings.FastEnabled
            ? PublishStream(fast, settings.FastPublishIntervalMs, fastMaximumAge, false, stop) : Task.CompletedTask;
        var slowTask = settings.SlowEnabled
            ? PublishStream(slow, settings.SlowPublishIntervalMs, slowMaximumAge, true, stop) : Task.CompletedTask;
        try { await Task.WhenAll(fastTask, slowTask).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { lock (_metricsLock) _finished = _time.GetTimestamp(); }
    }

    private async Task PublishStream(LatestMeterSnapshotQueue queue, int windowMs, TimeSpan maximumAge,
        bool slow, CancellationTokenSource stop)
    {
        try
        {
            while (true)
            {
                MeterWindowSnapshot snapshot;
                try { snapshot = await queue.ReadFreshAsync(maximumAge, stop.Token).ConfigureAwait(false); }
                catch (ChannelClosedException) { return; }
                stop.Token.ThrowIfCancellationRequested();
                if (!_client.IsConnected) throw new InvalidOperationException("MQTT disconnected during telemetry publishing.");
                string payload;
                lock (_serialization)
                {
                    if (!snapshot.IsFresh(maximumAge)) { queue.RecordStaleDrop(); continue; }
                    payload = slow ? _frames.BuildSlowFrame(snapshot, windowMs, _time.GetUtcNow())
                        : _frames.BuildFastFrame(snapshot, windowMs, _time.GetUtcNow());
                }
                if (!snapshot.IsFresh(maximumAge)) { queue.RecordStaleDrop(); continue; }
                stop.Token.ThrowIfCancellationRequested();
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(_baseTopic + (slow ? "/v2/meters/slow" : "/v2/meters/fast"))
                    .WithPayload(payload).WithRetainFlag(false)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build();
                // Await the actual send, never abandon it and create another in-flight send.
                var result = await _client.PublishAsync(message, stop.Token).ConfigureAwait(false);
                if (result.ReasonCode != MqttClientPublishReasonCode.Success)
                    throw new InvalidOperationException($"MQTT rejected telemetry: {result.ReasonCode}.");
                double? ageAtSendMs = snapshot.GetAge()?.TotalMilliseconds;
                lock (_metricsLock)
                {
                    if (slow) { _slowPublished++; _lastSlowAgeAtSendMs = ageAtSendMs; }
                    else { _fastPublished++; _lastFastAgeAtSendMs = ageAtSendMs; }
                }
            }
        }
        catch (OperationCanceledException error) when (!stop.IsCancellationRequested)
        {
            RequestSessionStop?.Invoke();
            stop.Cancel();
            throw new InvalidOperationException("MQTT cancelled telemetry without a session cancellation request.", error);
        }
        catch
        {
            RequestSessionStop?.Invoke();
            stop.Cancel(); // A fault ends both readers; the caller owns reconnect and sampler shutdown.
            throw;
        }
    }
}
