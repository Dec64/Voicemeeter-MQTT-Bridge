namespace VoicemeeterMqttBridge.Tests;
public sealed class AdvancedControlTests
{
    [Fact]
    public void Virtual_strips_have_tone_eq_but_no_physical_processing()
    {
        var entries = AdvancedControlRegistry.Build("strip:5", ["compressor", "gate", "denoiser", "eq", "eq_cells", "mono"]);
        Assert.Contains(entries, e => e.Parameter == "Strip[5].EQGain1");
        Assert.DoesNotContain(entries, e => e.Group is "compressor" or "gate" or "denoiser" or "eq_cells");
    }
    [Fact]
    public void Physical_and_bus_eq_cells_have_documented_bounds_and_unique_safe_ids()
    {
        var physical = AdvancedControlRegistry.Build("strip:0", ["compressor", "gate", "denoiser", "eq_cells"]);
        Assert.Contains(physical, e => e.Parameter == "Strip[0].Comp.Threshold" && e.Min == -40 && e.Max == -3);
        Assert.Contains(physical, e => e.Parameter == "Strip[0].EQ.channel[1].cell[5].f" && e.Min == 20 && e.Max == 20000);
        var bus = AdvancedControlRegistry.Build("bus:7", ["eq_cells", "compressor", "mono"]);
        Assert.Contains(bus, e => e.Parameter == "Bus[7].EQ.channel[7].cell[5].q" && e.Min == 1 && e.Max == 100);
        Assert.DoesNotContain(bus, e => e.Group == "compressor");
        Assert.Equal(bus.Count, bus.Select(e => e.Id).Distinct().Count());
    }
    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-41)] [InlineData(0)]
    public void New_commands_reject_values_outside_vendor_bounds(float value)
    {
        var entry = AdvancedControlRegistry.Build("strip:0", ["compressor"]).Single(e => e.Parameter == "Strip[0].Comp.Threshold");
        Assert.False(entry.Accepts(value));
    }
    [Fact]
    public void Probing_omits_unsupported_parameters_and_preserves_identity()
    {
        var remote = new FakeRemote(); remote.Parameters["Strip[5].EQGain1"] = 2;
        remote.FailedReads.Add("Strip[5].EQGain2");
        var entries = AdvancedControlRegistry.Build("strip:5", ["eq"]);
        var available = AdvancedControlRegistry.Probe(entries, remote);
        Assert.Contains(available, e => e.Parameter == "Strip[5].EQGain1" && e.SourceId == "strip:5");
        Assert.DoesNotContain(available, e => e.Parameter == "Strip[5].EQGain2");
    }
}
