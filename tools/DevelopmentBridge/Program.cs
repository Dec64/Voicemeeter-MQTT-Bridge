using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using VoicemeeterMqttBridge;

try
{
    var launch = DevelopmentLaunch.Parse(args);
    var settings = launch.LoadSettings();
    Console.WriteLine(DevelopmentLaunch.Summary(settings));
    if (!launch.Run) return 0; // No native/MQTT initialization or config writes on this path.

    string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings.EffectiveClientId)));
    using var instance = new Mutex(true, @"Local\VoicemeeterDevelopment_" + identity, out bool created);
    if (!created) throw new InvalidOperationException("A development run with this client ID is already active.");

    using var stop = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
    Console.CancelKeyPress += cancel;
    using var observer = new MqttFactory().CreateMqttClient();
    var observation = new DevelopmentStreamObservation(settings.EffectiveBaseTopic);
    long available = 0, unavailable = 0;
    observer.ApplicationMessageReceivedAsync += message =>
    {
        observation.Observe(message.ApplicationMessage.Topic, message.ApplicationMessage.PayloadSegment,
            message.ApplicationMessage.Retain);
        if (message.ApplicationMessage.Topic.EndsWith("/meters/fast") || message.ApplicationMessage.Topic.EndsWith("/meters/slow"))
        {
            try
            {
                using var doc = JsonDocument.Parse(message.ApplicationMessage.PayloadSegment);
                foreach (var source in doc.RootElement.GetProperty("sources").EnumerateObject())
                    if (source.Value.GetProperty("available").GetBoolean()) Interlocked.Increment(ref available);
                    else Interlocked.Increment(ref unavailable);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
        }
        return Task.CompletedTask;
    };
    BridgeService? bridge = null;
    try
    {
        var options = new MqttClientOptionsBuilder().WithTcpServer(settings.MqttHost, settings.MqttPort)
            .WithClientId(settings.EffectiveClientId + "-observer").WithCleanSession().WithTimeout(TimeSpan.FromSeconds(5));
        if (!string.IsNullOrEmpty(settings.MqttUsername)) options.WithCredentials(settings.MqttUsername, settings.MqttPassword);
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        await observer.ConnectAsync(options.Build(), connectTimeout.Token);
        var subscriptions = new MqttClientSubscribeOptionsBuilder();
        foreach (string suffix in new[] { "/metadata", "/meters/fast", "/meters/slow" })
            subscriptions.WithTopicFilter(settings.EffectiveBaseTopic + "/v2" + suffix, MqttQualityOfServiceLevel.AtLeastOnce);
        var subscribed = await observer.SubscribeAsync(subscriptions.Build(), connectTimeout.Token);
        if (subscribed.Items.Any(item => (int)item.ResultCode >= 128)) throw new InvalidOperationException("Observer subscription rejected.");

        bridge = new BridgeService(settings, log: line =>
        {
            if (line.StartsWith("V2 telemetry:", StringComparison.Ordinal)) Console.Error.WriteLine(line);
        });
        // The deadline includes startup/retry time; an unreachable publisher cannot run indefinitely.
        var duration = Task.Delay(TimeSpan.FromSeconds(launch.Seconds), stop.Token);
        var startup = bridge.StartAsync();
        if (await Task.WhenAny(startup, duration) == startup) await startup;
        try { await duration; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    finally
    {
        if (bridge is not null) await bridge.StopAsync();
        Console.CancelKeyPress -= cancel;
        if (observer.IsConnected)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await observer.DisconnectAsync(new MqttClientDisconnectOptions(), deadline.Token);
        }
    }
    var result = observation.Snapshot();
    Console.WriteLine(JsonSerializer.Serialize(new { metadata_received = result.Metadata, fast_received = result.Fast,
        slow_received = result.Slow, rejected = result.Rejected, available_source_readings = available,
        unavailable_source_readings = unavailable, requested_seconds = launch.Seconds,
        observer_timing = observation.TimingSnapshot() }));
    return result.Metadata > 0 && result.Rejected == 0 && available > 0 &&
        (!settings.MeteringV2.FastEnabled || result.Fast > 0) && (!settings.MeteringV2.SlowEnabled || result.Slow > 0) ? 0 : 2;
}
catch (Exception error)
{
    // Exceptions from config/network code can contain secrets; print only the error category.
    Console.Error.WriteLine("Development run failed: " + error.GetType().Name);
    Console.Error.WriteLine("Usage: DevelopmentBridge --config PATH --dry-run | --config PATH --run SECONDS (1–1800).");
    return 2;
}
