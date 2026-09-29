namespace VoicemeeterMqttBridge.Tests;

public sealed class ChannelMappingTests
{
    // Independent expectations from blueprint §3.2/3.3. Never derive these from the map under test.
    private static readonly int[][] Inputs =
    [
        [0, 1], [2, 3], [4, 5], [6, 7], [8, 9],
        [10, 11, 12, 13, 14, 15, 16, 17],
        [18, 19, 20, 21, 22, 23, 24, 25],
        [26, 27, 28, 29, 30, 31, 32, 33]
    ];
    private static readonly int[][] Outputs =
    [
        [0, 1, 2, 3, 4, 5, 6, 7], [8, 9, 10, 11, 12, 13, 14, 15],
        [16, 17, 18, 19, 20, 21, 22, 23], [24, 25, 26, 27, 28, 29, 30, 31],
        [32, 33, 34, 35, 36, 37, 38, 39], [40, 41, 42, 43, 44, 45, 46, 47],
        [48, 49, 50, 51, 52, 53, 54, 55], [56, 57, 58, 59, 60, 61, 62, 63]
    ];

    public static IEnumerable<object[]> Sources()
    {
        for (int i = 0; i < 8; i++)
        {
            yield return [SourceKind.Strip, i, Inputs[i]];
            yield return [SourceKind.Bus, i, Outputs[i]];
        }
    }

    public static IEnumerable<object[]> Channels()
    {
        for (int i = 0; i < 8; i++)
        {
            foreach (int mode in new[] { 0, 1, 2 })
                foreach (int channel in Inputs[i]) yield return [SourceKind.Strip, i, mode, channel];
            foreach (int channel in Outputs[i]) yield return [SourceKind.Bus, i, 3, channel];
        }
    }

    private static FakeRemote SilentRemote()
    {
        var remote = new FakeRemote();
        for (int mode = 0; mode < 3; mode++)
            for (int ch = 0; ch < 34; ch++) remote.Levels[(mode, ch)] = 0;
        for (int ch = 0; ch < 64; ch++) remote.Levels[(3, ch)] = 0;
        return remote;
    }

    [Theory]
    [MemberData(nameof(Sources))]
    [Trait("Requirement", "MAP-01/02/03")]
    public void Canonical_map_has_exact_channel_ranges(SourceKind kind, int index, int[] expected)
    {
        var source = PotatoChannelMap.Get(kind, index);
        Assert.Equal(expected, source.Channels);
        Assert.Equal($"{(kind == SourceKind.Strip ? "strip" : "bus")}:{index}", source.Id);
        Assert.Equal(kind, source.Kind);
        Assert.Equal(index, source.Index);
    }

    [Fact]
    public void All_sources_have_unique_canonical_ids_and_each_channel_has_exactly_one_owner()
    {
        Assert.Equal(16, PotatoChannelMap.All.Count);
        Assert.Equal(16, PotatoChannelMap.All.Select(s => s.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 34), PotatoChannelMap.All.Where(s => s.Kind == SourceKind.Strip).SelectMany(s => s.Channels));
        Assert.Equal(Enumerable.Range(0, 64), PotatoChannelMap.All.Where(s => s.Kind == SourceKind.Bus).SelectMany(s => s.Channels));
    }

    [Theory]
    [MemberData(nameof(Channels))]
    [Trait("Requirement", "MAP-01/02/03")]
    public void Every_channel_is_measured_only_by_its_owner_and_selected_tap(
        SourceKind kind, int owner, int mode, int hotChannel)
    {
        var remote = SilentRemote();
        remote.Levels[(mode, hotChannel)] = 0.75f;
        var sampler = new SourcePeakSampler(remote);
        // Check every source and every tap, not only the expected winning source.
        foreach (var source in PotatoChannelMap.All)
        {
            int[] modes = source.Kind == SourceKind.Strip ? [0, 1, 2] : [3];
            foreach (int candidateMode in modes)
            {
                remote.Reads.Clear();
                var sample = sampler.Sample(source.Kind, source.Index, (MeterTap)candidateMode);
                float expected = source.Kind == kind && source.Index == owner && candidateMode == mode ? 0.75f : 0;
                Assert.True(sample.Available);
                Assert.Equal(expected, sample.LinearPeak);
                Assert.Equal(source.Id, sample.SourceId);
                Assert.Equal((MeterTap)candidateMode, sample.Tap);
                int[] channels = source.Kind == SourceKind.Strip ? Inputs[source.Index] : Outputs[source.Index];
                Assert.Equal(channels.Select(ch => (candidateMode, ch)), remote.Reads);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Channels))]
    [Trait("Requirement", "MAP-04")]
    public void An_unavailable_channel_invalidates_only_its_own_source_and_tap(
        SourceKind kind, int owner, int mode, int failedChannel)
    {
        var remote = SilentRemote();
        remote.Levels.Remove((mode, failedChannel));
        var sampler = new SourcePeakSampler(remote);
        foreach (var source in PotatoChannelMap.All)
        {
            int[] modes = source.Kind == SourceKind.Strip ? [0, 1, 2] : [3];
            foreach (int candidateMode in modes)
            {
                var sample = sampler.Sample(source.Kind, source.Index, (MeterTap)candidateMode);
                bool affected = source.Kind == kind && source.Index == owner && candidateMode == mode;
                Assert.Equal(!affected, sample.Available);
                Assert.Equal(affected ? (float?)null : 0f, sample.LinearPeak);
                Assert.Equal(affected ? (double?)null : -90d, sample.Dbfs);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    [Trait("Requirement", "DB-02")]
    public void Combined_peak_uses_all_channels_and_never_reuses_a_previous_read(
        SourceKind kind, int index, int[] channels)
    {
        var remote = SilentRemote();
        int mode = kind == SourceKind.Strip ? 0 : 3;
        remote.Levels[(mode, channels[0])] = 0.1f;
        remote.Levels[(mode, channels[^1])] = 0.5f;
        var sampler = new SourcePeakSampler(remote);
        var peak = sampler.Sample(kind, index, (MeterTap)mode);
        Assert.Equal(0.5f, peak.LinearPeak);
        Assert.Equal(-6.020599913, peak.Dbfs!.Value, 8);

        remote.Levels.Remove((mode, channels[0]));
        var failed = sampler.Sample(kind, index, (MeterTap)mode);
        Assert.False(failed.Available);
        Assert.Null(failed.LinearPeak);
        Assert.Null(failed.Dbfs);

        foreach (int channel in channels) remote.Levels[(mode, channel)] = 0;
        var silent = sampler.Sample(kind, index, (MeterTap)mode);
        Assert.True(silent.Available);
        Assert.Equal(0f, silent.LinearPeak);
        Assert.Equal(-90d, silent.Dbfs);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-0.1f)]
    [Trait("Requirement", "MAP-04/DB-01")]
    public void Invalid_channel_values_are_unavailable_even_with_a_valid_sibling(float invalid)
    {
        var remote = SilentRemote();
        remote.Levels[(0, 0)] = invalid;
        remote.Levels[(0, 1)] = 0.75f;
        var sample = new SourcePeakSampler(remote).Sample(SourceKind.Strip, 0, MeterTap.PreFader);
        Assert.False(sample.Available);
        Assert.Null(sample.LinearPeak);
        Assert.Null(sample.Dbfs);
    }

    [Fact]
    [Trait("Requirement", "MUTE-01")]
    public void Input_taps_have_independent_peaks_and_bus_uses_only_output()
    {
        var remote = SilentRemote();
        remote.Levels[(0, 33)] = 0.5f;
        remote.Levels[(1, 33)] = 0.1f;
        remote.Levels[(2, 33)] = 0;
        remote.Levels[(3, 63)] = 0.25f;
        var sampler = new SourcePeakSampler(remote);
        var pre = sampler.Sample(SourceKind.Strip, 7, MeterTap.PreFader);
        var post = sampler.Sample(SourceKind.Strip, 7, MeterTap.PostFader);
        var muted = sampler.Sample(SourceKind.Strip, 7, MeterTap.PostMute);
        var bus = sampler.Sample(SourceKind.Bus, 7, MeterTap.Output);
        Assert.Equal(0.5f, pre.LinearPeak);
        Assert.Equal(0.1f, post.LinearPeak);
        Assert.Equal(0f, muted.LinearPeak);
        Assert.Equal(0.25f, bus.LinearPeak);
        Assert.Equal(0.5f, pre.LinearPeak); // Later reads cannot overwrite an earlier sample.
    }

    [Theory]
    [InlineData(SourceKind.Strip, MeterTap.Output)]
    [InlineData(SourceKind.Bus, MeterTap.PreFader)]
    [InlineData(SourceKind.Bus, MeterTap.PostFader)]
    [InlineData(SourceKind.Bus, MeterTap.PostMute)]
    [InlineData(SourceKind.Strip, (MeterTap)99)]
    public void Unsupported_taps_fail_before_any_API_read(SourceKind kind, MeterTap tap)
    {
        var remote = SilentRemote();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourcePeakSampler(remote).Sample(kind, 0, tap));
        Assert.Empty(remote.Reads);
    }

    [Theory]
    [InlineData(SourceKind.Strip, -1)]
    [InlineData(SourceKind.Strip, 8)]
    [InlineData(SourceKind.Bus, -1)]
    [InlineData(SourceKind.Bus, 8)]
    [InlineData((SourceKind)99, 0)]
    public void Invalid_sources_are_rejected(SourceKind kind, int index)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PotatoChannelMap.Get(kind, index));

    [Theory]
    [InlineData(1f, -90d, 0d)]
    [InlineData(0.5f, -90d, -6.020599913d)]
    [InlineData(0.1f, -90d, -20d)]
    [InlineData(0f, -90d, -90d)]
    [InlineData(0f, -60d, -60d)]
    [InlineData(0.000001f, -90d, -90d)]
    [InlineData(2f, -90d, 6.020599913d)]
    [Trait("Requirement", "DB-01")]
    public void Linear_conversion_is_finite_and_floor_clamped(float linear, double floor, double expected)
        => Assert.Equal(expected, PeakMath.ToDbfs(linear, floor), 5);

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    public void Conversion_rejects_invalid_amplitude(float linear)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PeakMath.ToDbfs(linear));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1d)]
    public void Invalid_display_floors_are_rejected(double floor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PeakMath.ToDbfs(0.5f, floor));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourcePeakSampler(SilentRemote(), floor));
    }
}
