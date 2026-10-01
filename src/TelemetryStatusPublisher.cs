// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge;

public enum TelemetrySessionState { Starting, Running, Stopped, Faulted }

/// <summary>
/// Explicit, single-flight status publication for one session. The owner supplies lifecycle
/// observations; this class neither starts telemetry nor infers native engine health.
/// </summary>
public sealed class TelemetryStatusPublisher
{
    private readonly IMqttClient _client;
    private readonly string _topic;
    private readonly string _sessionId;
    private readonly string _bridgeVersion;
    private readonly TimeProvider _time;
    private int _publishing;
    internal Func<MeterPublishDiagnostics>? ReadDiagnostics { get; init; }

    public TelemetryStatusPublisher(IMqttClient client, AggregateFrameBuilder frames,
        string baseTopic, string bridgeVersion, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseTopic);
        if (baseTopic.IndexOfAny(['+', '#', '\0']) >= 0)
            throw new ArgumentException("A publication topic cannot contain wildcards or NUL.", nameof(baseTopic));
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeVersion);
        _client = client;
        _topic = baseTopic + "/v2/status";
        _sessionId = frames.SessionId;
        _bridgeVersion = bridgeVersion;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task PublishAsync(TelemetrySessionState state, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (Interlocked.CompareExchange(ref _publishing, 1, 0) != 0)
            throw new InvalidOperationException("The previous status send must finish before another starts.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_client.IsConnected) throw new InvalidOperationException("Status requires a connected MQTT client.");
            var diagnostics = ReadDiagnostics?.Invoke();
            var payload = JsonSerializer.Serialize(new
            {
                schema = 2, session_id = _sessionId, bridge_version = _bridgeVersion,
                published_at_utc = _time.GetUtcNow().ToUniversalTime(),
                session_state = state.ToString().ToLowerInvariant(),
                // Unknown until supplied by measured native/runtime diagnostics. Never substitute configured rates.
                engine_state = (string?)null,
                actual_fast_hz = diagnostics is { ElapsedSeconds: > 0 } ? (double?)(diagnostics.FastPublishCount / diagnostics.ElapsedSeconds) : null,
                actual_slow_hz = diagnostics is { ElapsedSeconds: > 0 } ? (double?)(diagnostics.SlowPublishCount / diagnostics.ElapsedSeconds) : null,
                diagnostics,
                broker_connected_at_send = true
            });
            var message = new MqttApplicationMessageBuilder().WithTopic(_topic).WithPayload(payload)
                .WithRetainFlag(true).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build();
            var result = await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            if (result.ReasonCode != MqttClientPublishReasonCode.Success)
                throw new InvalidOperationException($"MQTT rejected telemetry status: {result.ReasonCode}.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!_client.IsConnected) throw new InvalidOperationException("MQTT disconnected during status publication.");
        }
        finally { Volatile.Write(ref _publishing, 0); }
    }
}
