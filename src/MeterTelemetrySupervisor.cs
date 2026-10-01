// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge;

/// <summary>
/// Runs one connected telemetry session at a time. Every explicit restart gets fresh
/// queues, windows and a builder/session ID. Does not own the client or native adapter,
/// reconnect automatically, or participate in the application's live startup path.
/// </summary>
public sealed class MeterTelemetrySupervisor
{
    private readonly IMqttClient _client;
    private readonly IVoicemeeterLevels _levels;
    private readonly TimeProvider _time;
    private int _running;

    public MeterTelemetrySupervisor(IMqttClient client, IVoicemeeterLevels levels, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(levels);
        _client = client;
        _levels = levels;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(SourceRegistry registry, MeteringV2Settings settings, string baseTopic, string bridgeVersion,
        TimeSpan fastMaximumAge, TimeSpan slowMaximumAge, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("The previous telemetry session must finish before another starts.");
        try
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.Enabled || (!settings.FastEnabled && !settings.SlowEnabled) ||
                !registry.Sources.Any(s => s.Enabled)) return;
            if (!_client.IsConnected) throw new InvalidOperationException("Telemetry requires a connected MQTT client.");

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var loop = new MeterTelemetryLoop(registry, settings, _levels, _time);
            var frames = new AggregateFrameBuilder(registry, settings);
            var publisher = new MeterTelemetryPublisher(_client, frames, baseTopic, _time)
            {
                RequestSessionStop = stop.Cancel
            };
            var metadata = new MqttApplicationMessageBuilder()
                .WithTopic(baseTopic + "/v2/metadata").WithPayload(frames.BuildMetadata(bridgeVersion))
                .WithRetainFlag(true).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build();
            Task sampling = Task.CompletedTask;
            var publishing = publisher.RunAsync(loop.Fast, loop.Slow, settings, fastMaximumAge, slowMaximumAge, stop.Token);
            try
            {
                // Reject publication configuration errors before creating the sampling timer.
                if (publishing.IsCompleted) { await publishing.ConfigureAwait(false); return; }
                stop.Token.ThrowIfCancellationRequested();
                var result = await _client.PublishAsync(metadata, stop.Token).ConfigureAwait(false);
                if (result.ReasonCode != MqttClientPublishReasonCode.Success)
                    throw new InvalidOperationException($"MQTT rejected metadata: {result.ReasonCode}.");
                stop.Token.ThrowIfCancellationRequested();
                if (!_client.IsConnected) throw new InvalidOperationException("MQTT disconnected during metadata publication.");
                sampling = loop.RunAsync(stop.Token);
                await Task.WhenAny(sampling, publishing).ConfigureAwait(false);
            }
            finally
            {
                stop.Cancel();
                // Do not release the restart gate while native work or a real send is still running.
                await Task.WhenAll(sampling, publishing).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { Volatile.Write(ref _running, 0); }
    }
}
