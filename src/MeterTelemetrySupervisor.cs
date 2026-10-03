// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Runtime.ExceptionServices;

namespace VoicemeeterMqttBridge;

/// <summary>Explicit opt-in to new slow sensor discovery; does not alter legacy discovery.</summary>
public sealed record SlowDiscoveryOptions(string Computer, string Prefix, int ExpireAfterSeconds);

/// <summary>Opt-in lifecycle status. Timeout requests cancellation; real sends are still drained.</summary>
public sealed record TelemetryStatusOptions(TimeSpan ShutdownTimeout);

/// <summary>
/// Runs one connected telemetry session at a time. Every explicit restart gets fresh
/// queues, windows and a builder/session ID. Does not own the client or native adapter,
/// reconnect automatically. MeterTelemetryRuntime owns application startup and reconnect.
/// </summary>
public sealed class MeterTelemetrySupervisor
{
    private readonly IMqttClient _client;
    private readonly IVoicemeeterLevels _levels;
    private readonly TimeProvider _time;
    private int _running;
    private Func<MeterPublishDiagnostics>? _diagnostics;
    public string DiagnosticsJson => _diagnostics is null ? "No sampling counters yet" : System.Text.Json.JsonSerializer.Serialize(_diagnostics(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    public MeterTelemetrySupervisor(IMqttClient client, IVoicemeeterLevels levels, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(levels);
        _client = client;
        _levels = levels;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(SourceRegistry registry, MeteringV2Settings settings, string baseTopic, string bridgeVersion,
        TimeSpan fastMaximumAge, TimeSpan slowMaximumAge, CancellationToken cancellationToken = default,
        SlowDiscoveryOptions? discovery = null, TelemetryStatusOptions? status = null)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("The previous telemetry session must finish before another starts.");
        try
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            if (status is not null && (status.ShutdownTimeout <= TimeSpan.Zero ||
                status.ShutdownTimeout > TimeSpan.FromMinutes(1)))
                throw new ArgumentOutOfRangeException(nameof(status), "Status shutdown timeout must be positive and at most one minute.");
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.Enabled || (!settings.FastEnabled && !settings.SlowEnabled) ||
                !registry.Sources.Any(s => s.Enabled)) return;
            if (!_client.IsConnected) throw new InvalidOperationException("Telemetry requires a connected MQTT client.");
            // Build the entire set before publishing anything, so invalid options cannot partially configure HA.
            var configs = discovery is not null && settings.SlowEnabled
                ? SlowSensorDiscovery.Build(registry, settings, discovery.Computer, baseTopic,
                    discovery.Prefix, bridgeVersion, discovery.ExpireAfterSeconds)
                : Array.Empty<MqttApplicationMessage>();

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var loop = new MeterTelemetryLoop(registry, settings, _levels, _time);
            var frames = new AggregateFrameBuilder(registry, settings);
            int discoveryPublished = 0;
            var publisher = new MeterTelemetryPublisher(_client, frames, baseTopic, _time)
            {
                RequestSessionStop = stop.Cancel
            };
            _diagnostics = () => publisher.GetDiagnostics() with { Sampling = loop.GetDiagnostics(), RetainedDiscoveryPublishCount = Volatile.Read(ref discoveryPublished) };
            var sessionStatus = status is null ? null : new TelemetryStatusPublisher(_client, frames, baseTopic, bridgeVersion, _time)
            {
                ReadDiagnostics = () => publisher.GetDiagnostics() with
                {
                    Sampling = loop.GetDiagnostics(),
                    RetainedDiscoveryPublishCount = Volatile.Read(ref discoveryPublished)
                }
            };
            var metadata = new MqttApplicationMessageBuilder()
                .WithTopic(baseTopic + "/v2/metadata").WithPayload(frames.BuildMetadata(bridgeVersion))
                .WithRetainFlag(true).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build();
            Task sampling = Task.CompletedTask;
            Task statusRefresh = Task.CompletedTask;
            Exception? failure = null;
            bool statusAttempted = false;
            var publishing = publisher.RunAsync(loop.Fast, loop.Slow, settings, fastMaximumAge, slowMaximumAge, stop.Token);
            try
            {
                // Reject publication configuration errors before creating the sampling timer.
                if (publishing.IsCompleted) { await publishing.ConfigureAwait(false); return; }
                if (sessionStatus is not null)
                {
                    statusAttempted = true;
                    await sessionStatus.PublishAsync(TelemetrySessionState.Starting, stop.Token).ConfigureAwait(false);
                }
                await PublishStartupAsync(metadata, stop.Token).ConfigureAwait(false);
                foreach (var config in configs)
                {
                    await PublishStartupAsync(config, stop.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref discoveryPublished);
                }
                sampling = loop.RunAsync(stop.Token);
                if (sampling.IsCompleted) await sampling.ConfigureAwait(false);
                if (sessionStatus is not null)
                {
                    var runningStatus = sessionStatus.PublishAsync(TelemetrySessionState.Running, stop.Token);
                    if (await Task.WhenAny(runningStatus, sampling, publishing).ConfigureAwait(false) != runningStatus)
                        stop.Cancel();
                    await runningStatus.ConfigureAwait(false);
                    statusRefresh = RefreshStatusAsync(sessionStatus, stop.Token);
                    await Task.WhenAny(sampling, publishing, statusRefresh).ConfigureAwait(false);
                }
                else await Task.WhenAny(sampling, publishing).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException || !stop.IsCancellationRequested)
                    failure = ex;
            }
            finally
            {
                stop.Cancel();
                // Do not release the restart gate while native work or a real send is still running.
                try { await Task.WhenAll(sampling, publishing, statusRefresh).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    if (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                        failure ??= ex;
                }
            }
            // Use an independent budget: the session cancellation token is already canceled.
            // Never overlap a terminal status with unfinished sampling or meter/status sends.
            if (statusAttempted && _client.IsConnected)
            {
                using var deadline = new CancellationTokenSource(status!.ShutdownTimeout, _time);
                try
                {
                    await sessionStatus!.PublishAsync(failure is null ? TelemetrySessionState.Stopped : TelemetrySessionState.Faulted,
                        deadline.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var terminalFailure = new InvalidOperationException("Terminal telemetry status publication failed.", ex);
                    failure = failure is null ? terminalFailure : new AggregateException(failure, terminalFailure);
                }
            }
            if (failure is OperationCanceledException)
                throw new InvalidOperationException("Telemetry was canceled without a shutdown request.", failure);
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { Volatile.Write(ref _running, 0); }
    }

    private async Task RefreshStatusAsync(TelemetryStatusPublisher status, CancellationToken token)
    {
        try
        {
            while (true)
            {
                // Delay after each completed send: no catch-up burst or overlapping status messages.
                await Task.Delay(TimeSpan.FromSeconds(30), _time, token).ConfigureAwait(false);
                await status.PublishAsync(TelemetrySessionState.Running, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException("Status refresh was canceled without a shutdown request.", ex);
        }
    }

    private async Task PublishStartupAsync(MqttApplicationMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_client.IsConnected) throw new InvalidOperationException("MQTT disconnected during telemetry startup.");
        var result = await _client.PublishAsync(message, token).ConfigureAwait(false);
        if (result.ReasonCode != MqttClientPublishReasonCode.Success)
            throw new InvalidOperationException($"MQTT rejected telemetry startup: {result.ReasonCode}.");
        token.ThrowIfCancellationRequested();
        if (!_client.IsConnected) throw new InvalidOperationException("MQTT disconnected during telemetry startup.");
    }
}
