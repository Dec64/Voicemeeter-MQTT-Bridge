using System.Text.Json;

namespace VoicemeeterMqttBridge.Tests;

public sealed class DiscoveryStatusTests
{
    [Theory]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 0)]
    public async Task Discovery_count_reports_completed_configs_not_attempted_or_planned(bool fail, bool slow, int expected)
    {
        var broker = new TelemetryBroker(); var clock = new TelemetryTimerClock();
        var settings = Settings(); settings.SlowEnabled = slow;
        var observed = new List<(string State, int Count)>();
        broker.BeforeSend = (message, _) =>
        {
            if (fail && message.Topic.EndsWith("_active/config")) throw new IOException("discovery failed");
            if (message.Topic.EndsWith("/status"))
            {
                using var json = JsonDocument.Parse(message.PayloadSegment);
                observed.Add((json.RootElement.GetProperty("session_state").GetString()!,
                    json.RootElement.GetProperty("diagnostics").GetProperty("retained_discovery_publish_count").GetInt32()));
            }
            return Task.CompletedTask;
        };
        using var stop = new CancellationTokenSource();
        var run = Run(new(broker.Client, new Levels(), clock), settings, stop.Token);
        if (fail) await Assert.ThrowsAsync<IOException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        else { stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(("starting", 0), observed[0]);
        Assert.Equal((fail ? "faulted" : "stopped", expected), observed.Last());
        if (!fail) Assert.Contains(("running", expected), observed);
    }

    [Fact]
    public async Task Restart_resets_discovery_count_and_keeps_separate_session_identity()
    {
        var broker = new TelemetryBroker(); var clock = new TelemetryTimerClock();
        var starts = new List<(string? Session, int Count)>();
        broker.BeforeSend = (message, _) =>
        {
            if (!message.Topic.EndsWith("/status")) return Task.CompletedTask;
            using var json = JsonDocument.Parse(message.PayloadSegment);
            var root = json.RootElement;
            if (root.GetProperty("session_state").GetString() == "starting")
                starts.Add((root.GetProperty("session_id").GetString(),
                    root.GetProperty("diagnostics").GetProperty("retained_discovery_publish_count").GetInt32()));
            return Task.CompletedTask;
        };
        var supervisor = new MeterTelemetrySupervisor(broker.Client, new Levels(), clock);
        for (int i = 0; i < 2; i++)
        {
            using var stop = new CancellationTokenSource(); var run = Run(supervisor, Settings(), stop.Token);
            stop.Cancel(); await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(2, starts.Count); Assert.All(starts, s => Assert.Equal(0, s.Count));
        Assert.NotEqual(starts[0].Session, starts[1].Session);
    }

    private static MeteringV2Settings Settings() => new()
    {
        Enabled = true, FastEnabled = true, SlowEnabled = true,
        Sources = new() { new() { Id = "strip:0", Enabled = true } }
    };
    private static Task Run(MeterTelemetrySupervisor supervisor, MeteringV2Settings settings, CancellationToken token)
        => supervisor.RunAsync(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings,
            "test/pc", "2-dev", TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(3), token,
            new("pc", "homeassistant", 5), new(TimeSpan.FromSeconds(5)));
    private sealed class Levels : IVoicemeeterLevels
    {
        public float GetLevel(int type, int channel) => 1;
    }
}
