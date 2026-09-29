// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;

public sealed record EngineIdentity(int Type, Version Version);

/// <summary>
/// Metadata boundary only. A native implementation awaits the matching installed SDK.
/// Labels are read by canonical kind/index, never a user-supplied parameter expression.
/// </summary>
public interface IVoicemeeterMetadata
{
    EngineIdentity GetEngineIdentity();
    string? GetLabel(SourceKind kind, int index);
}

public sealed class SourceDescriptor
{
    public string Id { get; }
    public SourceKind Kind { get; }
    public int Index { get; }
    public IReadOnlyList<int> Channels { get; }
    public string? EngineLabel { get; }
    public string DisplayLabel { get; }
    public string? Alias { get; }
    public bool Enabled { get; }
    public IReadOnlyList<MeterTap> MeterTaps { get; }
    // No capabilities are asserted until a version-qualified parameter registry is probed.
    public IReadOnlyList<string> CapabilityGroups { get; } = Array.Empty<string>();

    internal SourceDescriptor(SourceChannels map, SourceProfile profile, string? engineLabel)
    {
        Id = map.Id;
        Kind = map.Kind;
        Index = map.Index;
        Channels = map.Channels;
        EngineLabel = string.IsNullOrWhiteSpace(engineLabel) ? null : engineLabel;
        string generic = map.Kind == SourceKind.Bus
            ? map.Index < 5 ? $"A{map.Index + 1}" : $"B{map.Index - 4}"
            : map.Index < 5 ? $"Hardware Input {map.Index + 1}" : $"Virtual Input {map.Index - 4}";
        DisplayLabel = string.IsNullOrWhiteSpace(profile.DisplayLabel) ? EngineLabel ?? generic : profile.DisplayLabel;
        Alias = string.IsNullOrEmpty(profile.Alias) ? null : profile.Alias;
        Enabled = profile.Enabled;
        MeterTaps = profile.ResolveTaps();
    }
}

public sealed class SourceRegistry
{
    public EngineIdentity Engine { get; }
    public IReadOnlyList<SourceDescriptor> Sources { get; }

    private SourceRegistry(EngineIdentity engine, IEnumerable<SourceDescriptor> sources)
    {
        Engine = engine;
        Sources = Array.AsReadOnly(sources.ToArray());
    }

    public static SourceRegistry Read(IVoicemeeterMetadata remote, MeteringV2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var engine = remote.GetEngineIdentity();
        // The reference manual covers 3.1.0.1; older/other major variants need separate qualification.
        if (engine.Type != 3 || engine.Version is null || engine.Version.Major != 3 || engine.Version < new Version(3, 1, 0, 1))
            throw new NotSupportedException("Source registry requires a reported Potato 3.x version at least 3.1.0.1.");
        var profiles = settings.Sources.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var sources = new List<SourceDescriptor>();
        foreach (var map in PotatoChannelMap.All)
        {
            var profile = profiles.GetValueOrDefault(map.Id) ?? new SourceProfile { Id = map.Id };
            string? label;
            try { label = remote.GetLabel(map.Kind, map.Index); }
            catch (InvalidOperationException) { label = null; }
            sources.Add(new(map, profile, label));
        }
        return new(engine, sources);
    }
}
