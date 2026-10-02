// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;

namespace VoicemeeterMqttBridge;

public sealed record DevelopmentLaunch(string ConfigPath, bool Run, int Seconds)
{
    public static DevelopmentLaunch Parse(string[] args)
    {
        if (args.Length == 3 && args[0] == "--config" && args[2] == "--dry-run")
            return new(Path.GetFullPath(args[1]), false, 0);
        if (args.Length == 4 && args[0] == "--config" && args[2] == "--run" &&
            int.TryParse(args[3], out int seconds) && seconds is >= 1 and <= 1800)
            return new(Path.GetFullPath(args[1]), true, seconds);
        throw new ArgumentException("Usage: DevelopmentBridge --config PATH --dry-run | --config PATH --run SECONDS (1–1800).");
    }

    public AppSettings LoadSettings()
    {
        // No AppSettings.Load/Save: an explicit bad/missing config must never fall back to AppData.
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath), AppSettings.JsonOptions())
            ?? throw new ArgumentException("Configuration must be an object.");
        if (string.IsNullOrWhiteSpace(settings.MqttHost) || settings.MqttPort is < 1 or > 65535)
            throw new ArgumentException("A broker host and valid port are required.");
        ValidateLiteral(settings.EffectiveClientId, "client ID");
        ValidateLiteral(settings.EffectiveBaseTopic, "base topic");
        if (settings.HomeAssistantDiscovery) ValidateLiteral(settings.HomeAssistantDiscoveryPrefix, "discovery prefix");
        settings.MeteringV2.Validate();
        if (!settings.MeteringV2.Enabled || (!settings.MeteringV2.FastEnabled && !settings.MeteringV2.SlowEnabled) ||
            !settings.MeteringV2.Sources.Any(s => s.Enabled))
            throw new ArgumentException("Enable v2, a stream and at least one canonical source.");
        if (settings.StartPotatoWithApp) throw new ArgumentException("Set startPotatoWithApp=false; this runner uses an already running engine.");
        return settings;
    }

    private static void ValidateLiteral(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 480 || value.Any(char.IsControl) ||
            value.IndexOfAny(['+', '#', '{', '}']) >= 0)
            throw new ArgumentException($"Invalid {field}.");
    }

    public static string Summary(AppSettings settings) => JsonSerializer.Serialize(new
    {
        client_id = settings.EffectiveClientId, base_topic = settings.EffectiveBaseTopic,
        discovery_enabled = settings.HomeAssistantDiscovery, legacy_meters = settings.PublishMeters,
        fast_enabled = settings.MeteringV2.FastEnabled, slow_enabled = settings.MeteringV2.SlowEnabled,
        sample_ms = settings.MeteringV2.SampleIntervalMs, fast_ms = settings.MeteringV2.FastPublishIntervalMs,
        sources = settings.MeteringV2.Sources.Where(s => s.Enabled).Select(s => s.Id).ToArray()
    });
}

/// <summary>Small broker smoke-test counter; not a benchmark or full frontend frame validator.</summary>
public sealed class DevelopmentStreamObservation(string baseTopic)
{
    private readonly object _gate = new();
    private string? _session;
    private long _fastSequence = -1, _slowSequence = -1;
    private int _metadata, _fast, _slow, _rejected;

    public void Observe(string topic, ReadOnlyMemory<byte> payload, bool retained)
    {
        if (topic != baseTopic + "/v2/metadata" && topic != baseTopic + "/v2/meters/fast" &&
            topic != baseTopic + "/v2/meters/slow") return;
        lock (_gate)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                string? session = root.GetProperty("session_id").GetString();
                if (root.GetProperty("schema").GetInt32() != 2 || string.IsNullOrEmpty(session)) throw new FormatException();
                if (topic.EndsWith("/metadata"))
                {
                    if (_session != session) { _session = session; _fastSequence = _slowSequence = -1; }
                    _metadata++; return;
                }
                bool fast = topic.EndsWith("/fast");
                long sequence = root.GetProperty("seq").GetInt64();
                if (session != _session || (fast && retained) || sequence <= (fast ? _fastSequence : _slowSequence))
                    throw new FormatException();
                if (fast) { _fastSequence = sequence; _fast++; } else { _slowSequence = sequence; _slow++; }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            { _rejected++; }
        }
    }

    public (int Metadata, int Fast, int Slow, int Rejected) Snapshot()
    { lock (_gate) return (_metadata, _fast, _slow, _rejected); }
}
