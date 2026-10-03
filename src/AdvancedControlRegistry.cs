// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;
public sealed record AdvancedControl(string SourceId, string Id, string Parameter, string Group, string Name,
    float Min, float Max, float Step, string? Unit = null, bool Switch = false, bool Integer = false)
{
    public bool Accepts(float value) => float.IsFinite(value) && value >= Min && value <= Max && (!Integer || value == MathF.Truncate(value));
    public string UniqueId(string computer) => "voicemeeter_" + computer + "_v2_" + Id;
}
public static class AdvancedControlRegistry
{
    public static readonly string[] Groups = ["mono", "compressor", "gate", "denoiser", "eq", "eq_cells"];
    public static IReadOnlyList<AdvancedControl> Build(string id, IEnumerable<string> groups)
    {
        var source = PotatoChannelMap.Get(id); var enabled = groups.ToHashSet(StringComparer.Ordinal);
        if (enabled.Except(Groups).Any()) throw new ArgumentException("Unknown advanced control group.");
        string prefix = (source.Kind == SourceKind.Bus ? "Bus" : "Strip") + "[" + source.Index + "]";
        bool physical = source.Kind == SourceKind.Strip && source.Index < 5, bus = source.Kind == SourceKind.Bus;
        var result = new List<AdvancedControl>();
        void Add(string group, string suffix, string name, float min, float max, float step, string? unit = null, bool toggle = false, bool integer = false)
        {
            if (!enabled.Contains(group)) return;
            string safe = id.Replace(':','_') + "_" + suffix.ToLowerInvariant().Replace('.', '_').Replace('[', '_').Replace("]", "");
            result.Add(new(id, safe, prefix + "." + suffix, group, name, min, max, step, unit, toggle, integer || toggle));
        }
        Add("mono", "Mono", "Mono", 0, 1, 1, toggle: true);
        if (physical)
        {
            Add("compressor", "Comp", "Compression", 0, 10, .1f);
            Add("compressor", "Comp.GainIn", "Input gain", -24, 24, .1f, "dB");
            Add("compressor", "Comp.Ratio", "Ratio", 1, 8, .1f);
            Add("compressor", "Comp.Threshold", "Threshold", -40, -3, .1f, "dB");
            Add("compressor", "Comp.Attack", "Attack", 0, 200, 1, "ms");
            Add("compressor", "Comp.Release", "Release", 0, 5000, 1, "ms");
            Add("compressor", "Comp.Knee", "Knee", 0, 1, .01f);
            Add("compressor", "Comp.GainOut", "Output gain", -24, 24, .1f, "dB");
            Add("compressor", "Comp.MakeUp", "Automatic makeup", 0, 1, 1, toggle: true);
            Add("gate", "Gate", "Gate", 0, 10, .1f);
            Add("gate", "Gate.Threshold", "Threshold", -60, -10, .1f, "dB");
            Add("gate", "Gate.Damping", "Maximum damping", -60, -10, .1f, "dB");
            Add("gate", "Gate.BPSidechain", "Sidechain band pass", 100, 4000, 1, "Hz");
            Add("gate", "Gate.Attack", "Attack", 0, 1000, 1, "ms");
            Add("gate", "Gate.Hold", "Hold", 0, 5000, 1, "ms");
            Add("gate", "Gate.Release", "Release", 0, 5000, 1, "ms");
            Add("denoiser", "Denoiser", "Native denoiser", 0, 10, .1f);
            Add("denoiser", "Denoiser.Threshold", "Noise floor threshold", 0, 10, .1f);
        }
        if (!physical && !bus) for (int band = 1; band <= 3; band++) Add("eq", "EQGain" + band, "EQ band " + band, -12, 12, .1f, "dB");
        else
        {
            Add("eq", "EQ.on", "EQ enabled", 0, 1, 1, toggle: true);
            Add("eq", "EQ.AB", "EQ memory B", 0, 1, 1, toggle: true);
            for (int channel = 0; channel < 8; channel++) for (int cell = 0; cell < 6; cell++)
            {
                string path = "EQ.channel[" + channel + "].cell[" + cell + "]", label = "Ch " + (channel+1) + " cell " + (cell+1) + " ";
                Add("eq_cells", path + ".on", label + "enabled", 0, 1, 1, toggle: true);
                Add("eq_cells", path + ".type", label + "type", 0, 6, 1, integer: true);
                Add("eq_cells", path + ".f", label + "frequency", 20, 20000, 1, "Hz");
                Add("eq_cells", path + ".gain", label + "gain", -12, 12, .1f, "dB");
                Add("eq_cells", path + ".q", label + "quality", 1, 100, .1f);
            }
        }
        return result.AsReadOnly();
    }
    public static IReadOnlyList<AdvancedControl> Probe(IEnumerable<AdvancedControl> candidates, IVoicemeeterRemote remote)
    {
        var result = new List<AdvancedControl>();
        foreach (var entry in candidates) { try { if (entry.Accepts(remote.GetParameterFloat(entry.Parameter))) result.Add(entry); } catch (InvalidOperationException) { } }
        return result.AsReadOnly();
    }
}