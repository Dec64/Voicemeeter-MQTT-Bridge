using System.Text.Json;

namespace VoicemeeterMqttBridge.Tests;

public sealed class MeterWindowTests
{
    private readonly ManualTimeProvider _time = new();
    private static MeteringV2Settings Settings() => new()
    {
        ActivityThresholdDbfs = -20,
        Sources = new() { new() { Id = "strip:0", Enabled = true, MeterTaps = new() { "pre" } },
            new() { Id = "bus:7", Enabled = true } }
    };
    private MeterWindowAccumulator Window(MeteringV2Settings? settings = null)
    {
        settings ??= Settings();
        return new(SourceRegistry.Read(new SourceRegistryTests.Metadata(), settings), settings, _time);
    }
    private static SourcePeak Peak(float value, string id = "strip:0", MeterTap tap = MeterTap.PreFader)
        => new(id, tap, true, value, null);
    private static MeterWindowReading Strip(MeterWindowSnapshot snapshot)
        => snapshot.Readings.Single(r => r.Peak.SourceId == "strip:0");

    [Fact]
    [Trait("Requirement", "PEAK-01")]
    public void Independent_windows_preserve_brief_peak_and_reset_without_mutating_snapshots()
    {
        var fast = Window();
        var slow = Window();
        fast.Observe(Peak(1)); slow.Observe(Peak(1));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        var first = fast.CompleteWindow();
        fast.Observe(Peak(0.1f)); slow.Observe(Peak(0.1f));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(0.1f, Strip(fast.CompleteWindow()).Peak.LinearPeak);
        var longer = slow.CompleteWindow();
        Assert.Equal(1f, Strip(longer).Peak.LinearPeak);
        Assert.Equal(TimeSpan.FromMilliseconds(100), longer.Duration);
        Assert.Equal(1f, Strip(first).Peak.LinearPeak);
        Assert.Equal(TimeSpan.FromMilliseconds(50), first.Duration);
    }

    [Fact]
    [Trait("Requirement", "PEAK-02")]
    public void Activity_hysteresis_and_hold_expire_at_the_boundary()
    {
        var window = Window();
        window.Observe(Peak(0.2f));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(Strip(window.CompleteWindow()).Active);
        window.Observe(Peak(0.09f)); // Inside hysteresis band: remain active beyond the hold.
        _time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.True(Strip(window.CompleteWindow()).Active);
        window.Observe(Peak(0.2f)); // Renew hold at t=300.
        window.Observe(Peak(0.06f));
        _time.Advance(TimeSpan.FromMilliseconds(249));
        Assert.True(Strip(window.CompleteWindow()).Active);
        window.Observe(Peak(0.06f));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(Strip(window.CompleteWindow()).Active);
    }

    [Fact]
    public void Silence_is_immediately_inactive_but_observed_clip_has_a_short_hold()
    {
        var window = Window();
        window.Observe(Peak(1));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.True(Strip(window.CompleteWindow()).Clipping);
        window.Observe(Peak(0));
        _time.Advance(TimeSpan.FromMilliseconds(1949));
        var held = Strip(window.CompleteWindow());
        Assert.False(held.Active);
        Assert.True(held.Clipping);
        Assert.Equal(-90, held.Peak.Dbfs);
        window.Observe(Peak(0));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(Strip(window.CompleteWindow()).Clipping);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-1f)]
    public void Invalid_observation_invalidates_its_whole_window_only(float bad)
    {
        var window = Window();
        window.Observe(Peak(1));
        window.Observe(Peak(bad));
        window.Observe(Peak(0.25f, "bus:7", MeterTap.Output));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        var snapshot = window.CompleteWindow();
        var failed = Strip(snapshot);
        Assert.False(failed.Peak.Available);
        Assert.Null(failed.Peak.LinearPeak);
        Assert.Null(failed.Active);
        Assert.Null(failed.Clipping);
        Assert.True(snapshot.Readings.Single(r => r.Peak.SourceId == "bus:7").Peak.Available);
        window.Observe(Peak(0));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        var recovered = Strip(window.CompleteWindow());
        Assert.True(recovered.Peak.Available);
        Assert.False(recovered.Clipping);
        Assert.False(recovered.Active);
    }

    [Fact]
    public void Missing_tap_invalidates_source_and_empty_window_cannot_reuse_old_peak()
    {
        var settings = Settings();
        settings.Sources[0].MeterTaps = new() { "pre", "post_mute" };
        var window = Window(settings);
        window.Observe(Peak(1));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.All(window.CompleteWindow().Readings, r => Assert.False(r.Peak.Available));
        window.Observe(Peak(0));
        window.Observe(Peak(0, tap: MeterTap.PostMute));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.All(window.CompleteWindow().Readings.Where(r => r.Peak.SourceId == "strip:0"),
            r => { Assert.True(r.Peak.Available); Assert.False(r.Clipping); });
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.All(window.CompleteWindow().Readings, r => Assert.False(r.Peak.Available));
    }

    [Fact]
    public void Input_taps_keep_independent_peaks_and_activity()
    {
        var settings = Settings();
        settings.Sources[0].MeterTaps = new() { "pre", "post_mute" };
        var window = Window(settings);
        window.Observe(Peak(1));
        window.Observe(Peak(0, tap: MeterTap.PostMute));
        _time.Advance(TimeSpan.FromMilliseconds(50));
        var readings = window.CompleteWindow().Readings.Where(r => r.Peak.SourceId == "strip:0").ToArray();
        var incoming = readings.Single(r => r.Peak.Tap == MeterTap.PreFader);
        var muted = readings.Single(r => r.Peak.Tap == MeterTap.PostMute);
        Assert.Equal(1f, incoming.Peak.LinearPeak);
        Assert.True(incoming.Active);
        Assert.True(incoming.Clipping);
        Assert.Equal(0f, muted.Peak.LinearPeak);
        Assert.False(muted.Active);
        Assert.False(muted.Clipping);
    }

    [Fact]
    public void Unconfigured_samples_and_zero_duration_do_not_consume_window()
    {
        var window = Window();
        window.Observe(Peak(0.5f));
        Assert.Throws<ArgumentException>(() => window.Observe(Peak(1, "strip:1")));
        Assert.Throws<InvalidOperationException>(() => window.CompleteWindow());
        _time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(0.5f, Strip(window.CompleteWindow()).Peak.LinearPeak);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2000)]
    public void Exact_clip_threshold_is_on_even_with_zero_hysteresis(int hold)
    {
        var settings = Settings();
        settings.ClipThresholdDbfs = 0;
        settings.ClipHysteresisDb = 0;
        settings.ClipHoldMs = hold;
        var window = Window(settings);
        window.Observe(Peak(1));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(Strip(window.CompleteWindow()).Clipping);
    }

    [Fact]
    public void Clipping_hysteresis_keeps_on_in_band_and_releases_below_it()
    {
        var window = Window();
        window.Observe(Peak(1));
        window.Observe(Peak(0.97f)); // About -0.265 dBFS, inside [-0.6, -0.1].
        _time.Advance(TimeSpan.FromMilliseconds(2001));
        Assert.True(Strip(window.CompleteWindow()).Clipping);
        window.Observe(Peak(0.9f));
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(Strip(window.CompleteWindow()).Clipping);
    }

    [Fact]
    public void Default_and_custom_holds_round_trip_without_enabling_v2()
    {
        var migrated = JsonSerializer.Deserialize<AppSettings>("{}", AppSettings.JsonOptions())!;
        Assert.False(migrated.MeteringV2.Enabled);
        Assert.Equal(250, migrated.MeteringV2.ActivityHoldMs);
        Assert.Equal(2000, migrated.MeteringV2.ClipHoldMs);
        Assert.Equal(3, migrated.MeteringV2.ActivityHysteresisDb);
        Assert.Equal(0.5, migrated.MeteringV2.ClipHysteresisDb);
        migrated.MeteringV2.ActivityHoldMs = 0;
        migrated.MeteringV2.ClipHoldMs = 1500;
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(migrated, AppSettings.JsonOptions()), AppSettings.JsonOptions())!;
        Assert.Equal(0, restored.MeteringV2.ActivityHoldMs);
        Assert.Equal(1500, restored.MeteringV2.ClipHoldMs);
        Assert.False(restored.MeteringV2.Enabled);
    }

    [Theory]
    [InlineData("activityHoldMs", -1)]
    [InlineData("clipHoldMs", 60001)]
    [InlineData("activityHysteresisDb", -1)]
    [InlineData("clipHysteresisDb", 100)]
    [InlineData("clipHysteresisDb", 89.9)]
    public void Invalid_hold_settings_are_rejected(string key, double value)
    {
        var settings = JsonSerializer.Deserialize<MeteringV2Settings>($"{{\"{key}\":{value}}}")!;
        Assert.Throws<ArgumentException>(settings.Validate);
    }
}
