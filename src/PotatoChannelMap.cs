// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;

public enum SourceKind { Strip, Bus }
public enum MeterTap { PreFader = 0, PostFader = 1, PostMute = 2, Output = 3 }

public sealed class SourceChannels
{
    public string Id { get; }
    public SourceKind Kind { get; }
    public int Index { get; }
    public IReadOnlyList<int> Channels { get; }

    internal SourceChannels(SourceKind kind, int index, int firstChannel, int count)
    {
        Kind = kind;
        Index = index;
        Id = $"{(kind == SourceKind.Strip ? "strip" : "bus")}:{index}";
        Channels = Array.AsReadOnly(Enumerable.Range(firstChannel, count).ToArray());
    }
}

/// <summary>
/// Potato only: five stereo hardware strips, three eight-channel virtual strips,
/// and eight eight-channel buses. These are GetLevel channels, not callback buffers.
/// No user labels/aliases are inferred. See docs/SDK-REFERENCE.md.
/// </summary>
public static class PotatoChannelMap
{
    public static IReadOnlyList<SourceChannels> All { get; } = Array.AsReadOnly(
        Enumerable.Range(0, 8).Select(i => new SourceChannels(SourceKind.Strip, i,
            i < 5 ? i * 2 : 10 + (i - 5) * 8, i < 5 ? 2 : 8))
        .Concat(Enumerable.Range(0, 8).Select(i => new SourceChannels(SourceKind.Bus, i, i * 8, 8)))
        .ToArray());

    public static SourceChannels Get(SourceKind kind, int index)
    {
        if (kind is not (SourceKind.Strip or SourceKind.Bus)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (index is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(index));
        return All[(kind == SourceKind.Strip ? 0 : 8) + index];
    }

    public static SourceChannels Get(string id) => All.FirstOrDefault(s => s.Id == id)
        ?? throw new ArgumentException("Expected canonical strip:0–7 or bus:0–7.", nameof(id));
}
