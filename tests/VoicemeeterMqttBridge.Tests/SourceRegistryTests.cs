namespace VoicemeeterMqttBridge.Tests;

public sealed class SourceRegistryTests
{
    internal sealed class Metadata : IVoicemeeterMetadata
    {
        public EngineIdentity Identity { get; set; } = new(3, new Version(3, 1, 3, 0));
        public Dictionary<string, string?> Labels { get; } = new();
        public List<string> Reads { get; } = new();
        public EngineIdentity GetEngineIdentity() => Identity;
        public string? GetLabel(SourceKind kind, int index)
        {
            string id = $"{(kind == SourceKind.Strip ? "strip" : "bus")}:{index}";
            Reads.Add(id);
            if (!Labels.TryGetValue(id, out var label)) throw new InvalidOperationException("Unavailable label");
            return label;
        }
    }

    [Fact]
    public void Empty_profile_keeps_all_sources_disabled_and_does_not_invent_owner_aliases()
    {
        var registry = SourceRegistry.Read(new Metadata(), new MeteringV2Settings());
        Assert.Equal(16, registry.Sources.Count);
        Assert.All(registry.Sources, s => { Assert.False(s.Enabled); Assert.Null(s.Alias); Assert.Empty(s.CapabilityGroups); });
        Assert.Equal("Hardware Input 1", registry.Sources[0].DisplayLabel);
        Assert.Equal("Virtual Input 1", registry.Sources[5].DisplayLabel);
        Assert.Equal("A1", registry.Sources[8].DisplayLabel);
        Assert.Equal("B3", registry.Sources[15].DisplayLabel);
    }

    [Fact]
    [Trait("Requirement", "CAP-02 (metadata boundary only)")]
    public void Unicode_engine_label_and_override_are_distinct_and_never_change_canonical_identity()
    {
        var metadata = new Metadata();
        metadata.Labels["strip:5"] = "システム 🎵";
        var settings = new MeteringV2Settings { Sources = new()
        {
            new() { Id = "strip:5", Alias = "system", DisplayLabel = "My audio", Enabled = true }
        }};
        var registry = SourceRegistry.Read(metadata, settings);
        var source = registry.Sources.Single(s => s.Id == "strip:5");
        Assert.Equal("システム 🎵", source.EngineLabel);
        Assert.Equal("My audio", source.DisplayLabel);
        Assert.Equal("system", source.Alias);
        Assert.Equal(Enumerable.Range(10, 8), source.Channels);
        Assert.Equal(new[] { MeterTap.PreFader, MeterTap.PostMute }, source.MeterTaps);
        Assert.Equal(16, metadata.Reads.Distinct().Count());
        settings.Sources[0].DisplayLabel = "  ";
        metadata.Labels["strip:5"] = "Renamed";
        var renamed = SourceRegistry.Read(metadata, settings).Sources.Single(s => s.Id == "strip:5");
        Assert.Equal(source.Id, renamed.Id);
        Assert.Equal("Renamed", renamed.DisplayLabel);
        Assert.Equal("My audio", source.DisplayLabel); // Previous snapshot is immutable.
    }

    [Theory]
    [InlineData(1, "1.0.0.0")]
    [InlineData(2, "2.1.3.0")]
    [InlineData(3, "3.0.0.0")]
    [InlineData(3, "4.0.0.0")]
    public void Unqualified_engine_is_rejected_before_any_label_read(int type, string version)
    {
        var metadata = new Metadata { Identity = new(type, Version.Parse(version)) };
        Assert.Throws<NotSupportedException>(() => SourceRegistry.Read(metadata, new()));
        Assert.Empty(metadata.Reads);
    }

    [Theory]
    [InlineData("strip:0", "strip:0", "one", "two")]
    [InlineData("strip:0", "strip:1", "guest", "GUEST")]
    public void Duplicate_identity_or_alias_is_rejected_before_label_reads(string a, string b, string aliasA, string aliasB)
    {
        var settings = new MeteringV2Settings { Sources = new()
        {
            new() { Id = a, Alias = aliasA }, new() { Id = b, Alias = aliasB }
        }};
        var metadata = new Metadata();
        Assert.Throws<ArgumentException>(() => SourceRegistry.Read(metadata, settings));
        Assert.Empty(metadata.Reads);
    }

    [Theory]
    [InlineData("strip:8", "pre")]
    [InlineData("strip:01", "pre")]
    [InlineData("Strip:1", "pre")]
    [InlineData("bus:0", "pre")]
    [InlineData("strip:0", "output")]
    [InlineData("strip:0", "unknown")]
    public void Invalid_identity_or_tap_is_rejected(string id, string tap)
    {
        var settings = new MeteringV2Settings { Sources = new() { new() { Id = id, MeterTaps = new() { tap } } } };
        Assert.Throws<ArgumentException>(settings.Validate);
    }
}
