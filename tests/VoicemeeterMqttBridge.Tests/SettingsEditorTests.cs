using System.Text.Json;
namespace VoicemeeterMqttBridge.Tests;
public sealed class SettingsEditorTests
{
    [Fact]
    public void Atomic_save_keeps_the_previous_complete_settings_as_backup()
    {
        string folder = Path.Combine(Path.GetTempPath(), "bridge-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.json");
        try
        {
            var first = new AppSettings { MqttHost = "first.invalid" };
            SettingsFile.Save(first, path);
            string original = File.ReadAllText(path);
            SettingsFile.Save(new AppSettings { MqttHost = "second.invalid" }, path);
            Assert.Equal(original, File.ReadAllText(path + ".bak"));
            Assert.Equal("second.invalid", JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), AppSettings.JsonOptions())!.MqttHost);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Invalid_draft_cannot_replace_the_settings_file()
    {
        string folder = Path.Combine(Path.GetTempPath(), "bridge-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "settings.json");
        try
        {
            SettingsFile.Save(new AppSettings(), path);
            string original = File.ReadAllText(path);
            var invalid = new AppSettings(); invalid.MeteringV2.SampleIntervalMs = 0;
            Assert.Throws<ArgumentException>(() => SettingsFile.Save(invalid, path));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.False(File.Exists(path + ".bak"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Editing_basic_fields_preserves_v2_credentials_and_unknown_fields()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"mqttPassword":"synthetic", "future":{"keep":1}, "meteringV2":{"enabled":true,"sources":[{"id":"strip:7","enabled":true}]}}""", AppSettings.JsonOptions())!;
        var draft = SettingsEditor.Clone(settings);
        draft.MqttHost = "new.invalid";
        Assert.Equal("synthetic", draft.MqttPassword);
        Assert.True(draft.MeteringV2.Enabled);
        Assert.Equal("strip:7", Assert.Single(draft.MeteringV2.Sources).Id);
        Assert.True(draft.AdditionalSettings!["future"].GetProperty("keep").GetInt32() == 1);
        draft.MeteringV2.Sources.Clear();
        Assert.Single(settings.MeteringV2.Sources);
    }
    [Fact]
    public void Redacted_export_contains_only_validated_metering_without_root_secrets()
    {
        var settings = new AppSettings { MqttPassword = "secret", MqttUsername = "private", MqttHost = "private.invalid" };
        string exported = SettingsEditor.ExportMetering(settings.MeteringV2);
        Assert.DoesNotContain("secret", exported);
        Assert.DoesNotContain("private", exported);
        Assert.False(JsonDocument.Parse(exported).RootElement.GetProperty("meteringV2").GetProperty("enabled").GetBoolean());
    }
}
