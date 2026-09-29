// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;

/// <summary>Existing adapter boundary; production signatures and error handling are unchanged.</summary>
public interface IVoicemeeterLevels
{
    float GetLevel(int type, int channel);
}

public interface IVoicemeeterRemote : IVoicemeeterLevels
{
    bool IsLoaded { get; }
    void Load();
    int Login();
    int Logout();
    int RunVoicemeeter(int type);
    int IsParametersDirty();
    float GetParameterFloat(string parameter);
    int SetParameterFloat(string parameter, float value);
}
