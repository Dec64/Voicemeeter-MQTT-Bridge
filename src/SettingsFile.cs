// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;

namespace VoicemeeterMqttBridge;

public static class SettingsFile
{
    public static void Save(AppSettings settings, string path)
    {
        settings.MeteringV2.Validate();
        string json = JsonSerializer.Serialize(settings, AppSettings.JsonOptions(true));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
}
