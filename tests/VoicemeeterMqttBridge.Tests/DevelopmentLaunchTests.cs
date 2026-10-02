using System.Text;
using System.Text.Json;

namespace VoicemeeterMqttBridge.Tests;

public class DevelopmentLaunchTests
{
    [Fact]
    public void Arguments_require_explicit_mode_path_and_bounded_duration()
    {
        Assert.False(DevelopmentLaunch.Parse(new[] { "--config", "fixture.json", "--dry-run" }).Run);
        Assert.Equal(10, DevelopmentLaunch.Parse(new[] { "--config", "fixture.json", "--run", "10" }).Seconds);
        foreach (var args in new[] { Array.Empty<string>(), new[] { "--run" },
            new[] { "--config", "fixture.json", "--run", "0" }, new[] { "--config", "fixture.json", "--run", "1801" } })
            Assert.Throws<ArgumentException>(() => DevelopmentLaunch.Parse(args));
    }
    [Fact]
    public void Explicit_file_is_read_without_rewriting_and_summary_omits_credentials()
    {
        string path = Path.GetTempFileName();
        try
        {
            var settings = BridgeTelemetryTests.Settings();
            settings.MqttUsername = "private-user"; settings.MqttPassword = "private-password";
            string json = JsonSerializer.Serialize(settings); File.WriteAllText(path, json);
            var loaded = new DevelopmentLaunch(path, false, 0).LoadSettings();
            Assert.Equal("private-password", loaded.MqttPassword); Assert.Equal(json, File.ReadAllText(path));
            string summary = DevelopmentLaunch.Summary(loaded);
            Assert.DoesNotContain("private-user", summary); Assert.DoesNotContain("private-password", summary);
            settings.BaseTopic = "bad/#"; File.WriteAllText(path, JsonSerializer.Serialize(settings));
            Assert.Throws<ArgumentException>(() => new DevelopmentLaunch(path, false, 0).LoadSettings());
            File.WriteAllText(path, "malformed");
            Assert.Throws<JsonException>(() => new DevelopmentLaunch(path, false, 0).LoadSettings());
        }
        finally { File.Delete(path); }
        Assert.Throws<FileNotFoundException>(() => new DevelopmentLaunch(path, false, 0).LoadSettings());
    }
    [Fact]
    public void Broker_counter_requires_metadata_matching_session_and_increasing_sequence()
    {
        var counter = new DevelopmentStreamObservation("test");
        void Send(string topic, string payload, bool retained = false) => counter.Observe("test/v2/" + topic, Encoding.UTF8.GetBytes(payload), retained);
        Send("meters/fast", """{"schema":2,"session_id":"one","seq":0}""");
        Send("metadata", """{"schema":2,"session_id":"one"}""", true);
        Send("meters/fast", """{"schema":2,"session_id":"one","seq":0}""");
        Send("meters/fast", """{"schema":2,"session_id":"one","seq":0}""");
        Send("meters/fast", """{"schema":2,"session_id":"one","seq":1}""", true);
        Send("meters/slow", """{"schema":2,"session_id":"one","seq":1}""");
        Send("meters/fast", "malformed");
        Assert.Equal((1, 1, 1, 4), counter.Snapshot());
    }
}
