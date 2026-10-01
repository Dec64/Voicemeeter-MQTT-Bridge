// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;

namespace VoicemeeterMqttBridge;

/// <summary>Pure discovery payload construction. Does not publish or retire entities.</summary>
public static class SlowSensorDiscovery
{
    public static IReadOnlyList<MqttApplicationMessage> Build(SourceRegistry registry, MeteringV2Settings settings,
        string computer, string baseTopic, string discoveryPrefix, string bridgeVersion, int expireAfterSeconds)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(computer);
        if (!computer.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new ArgumentException("Computer identity must contain only ASCII letters, digits, underscores or hyphens.", nameof(computer));
        ValidateTopic(baseTopic);
        ValidateTopic(discoveryPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeVersion);
        if (expireAfterSeconds <= 0 || (long)expireAfterSeconds * 1000 <= settings.SlowPublishIntervalMs)
            throw new ArgumentOutOfRangeException(nameof(expireAfterSeconds), "Expiry must exceed the slow window.");
        var messages = new List<MqttApplicationMessage>();
        if (!settings.Enabled || !settings.SlowEnabled) return messages;
        string slowTopic = baseTopic + "/v2/meters/slow";
        foreach (var source in registry.Sources.Where(s => s.Enabled))
        {
            var tap = source.MeterTaps.Contains(MeterTap.PreFader) ? MeterTap.PreFader : source.MeterTaps[0];
            string tapName = MeterTapNames.Format(tap);
            foreach (string metric in new[] { "peak", "active", "clip" })
            {
                bool peak = metric == "peak";
                string field = peak ? tapName + "_dbfs" : metric == "clip" ? "clipping" : "active";
                string id = "voicemeeter_" + computer + "_v2_" + source.Id.Replace(':', '_') + "_" + metric;
                // Only registry-owned IDs and enum-derived fields enter template code. Labels stay JSON data.
                string prefix = "{% set f = value_json if value_json is defined and value_json is mapping else {} %}" +
                    "{% set sources = f.get('sources') %}" +
                    "{% set s = sources.get('" + source.Id + "') if sources is mapping else none %}" +
                    "{% set s = s if s is mapping else {} %}";
                string valid = "f.get('schema') == 2 and s.get('available') == true and s.get('sensor_tap') == '" + tapName +
                    "' and s.get('" + field + "') is " + (peak ? "number and s.get('" + field + "') is not boolean" : "boolean");
                string value = peak ? "s.get('" + field + "')" : "('ON' if s.get('" + field + "') else 'OFF')";
                var payload = new Dictionary<string, object?>
                {
                    ["name"] = source.DisplayLabel + (peak ? " Peak" : metric == "clip" ? " Clipping" : " Activity"),
                    ["unique_id"] = id, ["state_topic"] = slowTopic,
                    ["value_template"] = prefix + "{{ " + value + " if " + valid + " else none }}",
                    ["availability_mode"] = "all",
                    ["availability"] = new object[]
                    {
                        new { topic = baseTopic + "/availability", payload_available = "online", payload_not_available = "offline" },
                        new { topic = slowTopic, payload_available = "online", payload_not_available = "offline",
                            value_template = prefix + "{{ 'online' if " + valid + " else 'offline' }}" }
                    },
                    ["expire_after"] = expireAfterSeconds, ["qos"] = 0,
                    ["device"] = new { identifiers = new[] { "voicemeeter_mqtt_bridge_" + computer },
                        name = "Voicemeeter " + computer, manufacturer = "VB-Audio", model = "Voicemeeter Potato MQTT Bridge", sw_version = bridgeVersion }
                };
                if (peak) payload["unit_of_measurement"] = "dBFS";
                else { payload["payload_on"] = "ON"; payload["payload_off"] = "OFF"; }
                messages.Add(new MqttApplicationMessageBuilder()
                    .WithTopic(discoveryPrefix.Trim('/') + "/" + (peak ? "sensor" : "binary_sensor") + "/" + id + "/config")
                    .WithPayload(JsonSerializer.Serialize(payload)).WithRetainFlag(true)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
            }
        }
        return messages;
    }

    private static void ValidateTopic(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        if (topic.Trim('/').Length == 0 || topic.IndexOfAny(['+', '#', '\0']) >= 0)
            throw new ArgumentException("Topic must be nonempty without MQTT wildcards or NUL.", nameof(topic));
    }
}
