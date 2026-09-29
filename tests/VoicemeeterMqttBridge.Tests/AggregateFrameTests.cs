using System.Text.Json;

namespace VoicemeeterMqttBridge.Tests;

public sealed class AggregateFrameTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 29, 19, 0, 0, TimeSpan.FromHours(1));
    private static MeteringV2Settings Settings() => new() { Sources = new()
    {
        new() { Id = "strip:0", Enabled = true }, new() { Id = "strip:5", Enabled = true },
        new() { Id = "bus:7", Enabled = true }, new() { Id = "strip:1", Enabled = false }
    }};
    private static AggregateFrameBuilder Builder(MeteringV2Settings? settings = null)
    {
        settings ??= Settings();
        return new(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings);
    }
    private static SourcePeak Peak(string id, MeterTap tap, float linear) => new(id, tap, true, linear, PeakMath.ToDbfs(linear));
    private static SourcePeak[] Samples() => new[]
    {
        Peak("strip:0", MeterTap.PreFader, 0.5f), Peak("strip:0", MeterTap.PostMute, 0),
        Peak("strip:5", MeterTap.PreFader, 0.1f), Peak("strip:5", MeterTap.PostMute, 0.1f),
        Peak("bus:7", MeterTap.Output, 0.25f)
    };

    [Fact]
    [Trait("Requirement", "MQTT-01 (serialization only)")]
    public void One_frame_contains_only_enabled_canonical_sources_and_their_own_taps()
    {
        using var json = JsonDocument.Parse(Builder().BuildFastFrame(Samples(), 50, Timestamp));
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("schema").GetInt32());
        Assert.Equal(0, root.GetProperty("seq").GetInt64());
        Assert.Equal(TimeSpan.Zero, root.GetProperty("published_at_utc").GetDateTimeOffset().Offset);
        Assert.Equal(Timestamp, root.GetProperty("published_at_utc").GetDateTimeOffset());
        Assert.Equal(50, root.GetProperty("sample_window_ms").GetInt32());
        var sources = root.GetProperty("sources");
        Assert.Equal(new[] { "strip:0", "strip:5", "bus:7" }, sources.EnumerateObject().Select(p => p.Name));
        var physical = sources.GetProperty("strip:0");
        Assert.Equal(-6.0206, physical.GetProperty("pre_dbfs").GetDouble(), 4);
        Assert.Equal(-90, physical.GetProperty("post_mute_dbfs").GetDouble());
        Assert.True(physical.GetProperty("active").GetBoolean());
        Assert.False(physical.GetProperty("clipping").GetBoolean());
        Assert.False(physical.TryGetProperty("output_dbfs", out _));
        Assert.Equal(-20, sources.GetProperty("strip:5").GetProperty("pre_dbfs").GetDouble(), 4);
        var bus = sources.GetProperty("bus:7");
        Assert.Equal(-12.0412, bus.GetProperty("output_dbfs").GetDouble(), 4);
        Assert.False(bus.TryGetProperty("pre_dbfs", out _));
        Assert.False(bus.TryGetProperty("peak_hold", out _));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    [Trait("Requirement", "MQTT-03")]
    public void Invalid_read_invalidates_only_its_source_and_emits_nulls(float invalid)
    {
        var samples = Samples();
        samples[0] = new("strip:0", MeterTap.PreFader, true, invalid, 0);
        using var json = JsonDocument.Parse(Builder().BuildFastFrame(samples, 50, Timestamp));
        var sources = json.RootElement.GetProperty("sources");
        var failed = sources.GetProperty("strip:0");
        Assert.False(failed.GetProperty("available").GetBoolean());
        foreach (string field in new[] { "pre_dbfs", "post_mute_dbfs", "active", "clipping" })
            Assert.Equal(JsonValueKind.Null, failed.GetProperty(field).ValueKind);
        Assert.True(sources.GetProperty("strip:5").GetProperty("available").GetBoolean());
        Assert.True(sources.GetProperty("bus:7").GetProperty("available").GetBoolean());
    }

    [Fact]
    public void Missing_or_unavailable_tap_cannot_reuse_previous_frame_and_silence_is_valid()
    {
        var builder = Builder();
        builder.BuildFastFrame(Samples(), 50, Timestamp);
        var samples = new[] { Peak("strip:0", MeterTap.PreFader, 0), Peak("strip:0", MeterTap.PostMute, 0),
            new SourcePeak("strip:5", MeterTap.PreFader, false, null, null) };
        using var json = JsonDocument.Parse(builder.BuildFastFrame(samples, 50, Timestamp.AddMilliseconds(50)));
        var sources = json.RootElement.GetProperty("sources");
        Assert.True(sources.GetProperty("strip:0").GetProperty("available").GetBoolean());
        Assert.False(sources.GetProperty("strip:0").GetProperty("active").GetBoolean());
        Assert.False(sources.GetProperty("strip:5").GetProperty("available").GetBoolean());
        Assert.False(sources.GetProperty("bus:7").GetProperty("available").GetBoolean());
    }

    [Fact]
    [Trait("Requirement", "PEAK-01 (supplied samples only)")]
    public void Highest_observed_sample_is_preserved_independently_per_tap()
    {
        var samples = Samples().Concat(new[] { Peak("strip:0", MeterTap.PreFader, 1), Peak("strip:0", MeterTap.PostMute, 0.25f) });
        using var json = JsonDocument.Parse(Builder().BuildFastFrame(samples, 50, Timestamp));
        var source = json.RootElement.GetProperty("sources").GetProperty("strip:0");
        Assert.Equal(0, source.GetProperty("pre_dbfs").GetDouble());
        Assert.Equal(-12.0412, source.GetProperty("post_mute_dbfs").GetDouble(), 4);
        Assert.True(source.GetProperty("clipping").GetBoolean());
    }

    [Fact]
    public void Sequence_increases_and_new_process_builder_has_distinct_session()
    {
        var builder = Builder();
        using var first = JsonDocument.Parse(builder.BuildFastFrame(Samples(), 50, Timestamp));
        using var second = JsonDocument.Parse(builder.BuildFastFrame(Samples(), 50, Timestamp.AddSeconds(-1)));
        using var restarted = JsonDocument.Parse(Builder().BuildFastFrame(Samples(), 50, Timestamp));
        Assert.Equal(1, second.RootElement.GetProperty("seq").GetInt64());
        string session = first.RootElement.GetProperty("session_id").GetString()!;
        Assert.True(Guid.TryParse(session, out _));
        Assert.Equal(session, second.RootElement.GetProperty("session_id").GetString());
        Assert.NotEqual(session, restarted.RootElement.GetProperty("session_id").GetString());
    }

    [Fact]
    public void Metadata_has_stable_channels_and_no_unprobed_capabilities_or_ha_entity_guesses()
    {
        var builder = Builder();
        using var metadata = JsonDocument.Parse(builder.BuildMetadata("2.0.0-dev"));
        Assert.Equal("potato", metadata.RootElement.GetProperty("engine").GetString());
        var sources = metadata.RootElement.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal(16, sources.Length);
        var bus = sources.Single(s => s.GetProperty("id").GetString() == "bus:7");
        Assert.Equal(Enumerable.Range(56, 8), bus.GetProperty("channels").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Equal("output", Assert.Single(bus.GetProperty("taps").EnumerateArray()).GetString());
        Assert.All(sources, s => { Assert.Empty(s.GetProperty("capability_groups").EnumerateArray()); Assert.False(s.TryGetProperty("entity_id", out _)); });
        using var frame = JsonDocument.Parse(builder.BuildFastFrame(Samples(), 50, Timestamp));
        Assert.Equal(metadata.RootElement.GetProperty("session_id").GetString(), frame.RootElement.GetProperty("session_id").GetString());
    }

    [Theory]
    [InlineData("strip:1", MeterTap.PreFader)]
    [InlineData("strip:8", MeterTap.PreFader)]
    [InlineData("bus:7", MeterTap.PostMute)]
    [InlineData("strip:0", MeterTap.Output)]
    public void Samples_outside_the_enabled_source_taps_are_rejected(string id, MeterTap tap)
        => Assert.Throws<ArgumentException>(() => Builder().BuildFastFrame(new[] { Peak(id, tap, 0.1f) }, 50, Timestamp));

    [Fact]
    public void Failed_sample_later_in_window_invalidates_prior_good_readings()
    {
        var samples = Samples().Append(new SourcePeak("strip:0", MeterTap.PostMute, false, null, null));
        using var json = JsonDocument.Parse(Builder().BuildFastFrame(samples, 50, Timestamp));
        Assert.False(json.RootElement.GetProperty("sources").GetProperty("strip:0").GetProperty("available").GetBoolean());
    }

    [Fact]
    public void Registry_sampler_and_frame_compose_without_cross_source_reuse()
    {
        var settings = Settings();
        var registry = SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings);
        var remote = new FakeRemote();
        foreach (int mode in new[] { 0, 2 })
            for (int channel = 0; channel < 34; channel++) remote.Levels[(mode, channel)] = 0;
        for (int channel = 0; channel < 64; channel++) remote.Levels[(3, channel)] = 0;
        remote.Levels[(0, 0)] = 0.25f;
        remote.Levels[(0, 17)] = 0.5f;
        remote.Levels[(3, 63)] = 1;
        var sampler = new SourcePeakSampler(remote);
        var samples = registry.Sources.Where(s => s.Enabled)
            .SelectMany(s => s.MeterTaps.Select(t => sampler.Sample(s.Kind, s.Index, t)));
        using var json = JsonDocument.Parse(new AggregateFrameBuilder(registry, settings).BuildFastFrame(samples, 50, Timestamp));
        var sources = json.RootElement.GetProperty("sources");
        Assert.Equal(-12.0412, sources.GetProperty("strip:0").GetProperty("pre_dbfs").GetDouble(), 4);
        Assert.Equal(-6.0206, sources.GetProperty("strip:5").GetProperty("pre_dbfs").GetDouble(), 4);
        Assert.Equal(0, sources.GetProperty("bus:7").GetProperty("output_dbfs").GetDouble());
    }
}
