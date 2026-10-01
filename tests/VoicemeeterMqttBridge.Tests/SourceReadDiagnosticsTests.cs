namespace VoicemeeterMqttBridge.Tests;

public sealed class SourceReadDiagnosticsTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1.5f, 0)]
    [InlineData(-1f, 1)]
    [InlineData(float.NaN, 1)]
    [InlineData(float.PositiveInfinity, 1)]
    public void Counts_each_channel_and_distinguishes_invalid_values(float value, long invalid)
    {
        var sampler = new SourcePeakSampler(new Levels(channel => channel == 0 ? value : 0));
        var peak = sampler.Sample(SourceKind.Strip, 0, MeterTap.PreFader);
        Assert.Equal(new MeterSamplingDiagnostics(2, invalid), sampler.GetDiagnostics());
        Assert.Equal(invalid == 0, peak.Available);
        Assert.Equal(new MeterSamplingDiagnostics(0, 0), new SourcePeakSampler(new Levels(_ => 0)).GetDiagnostics());
    }

    [Fact]
    public void Expected_read_error_is_counted_but_other_channels_are_still_read()
    {
        var sampler = new SourcePeakSampler(new Levels(channel => channel == 0 ? throw new InvalidOperationException() : 1));
        Assert.False(sampler.Sample(SourceKind.Strip, 0, MeterTap.PreFader).Available);
        Assert.Equal(new MeterSamplingDiagnostics(2, 1), sampler.GetDiagnostics());
    }

    [Fact]
    public void Unexpected_read_error_is_counted_and_propagated_without_reading_remaining_channels()
    {
        var error = new IOException("native failed");
        var sampler = new SourcePeakSampler(new Levels(_ => throw error));
        Assert.Same(error, Assert.Throws<IOException>(() => sampler.Sample(SourceKind.Bus, 0, MeterTap.Output)));
        Assert.Equal(new MeterSamplingDiagnostics(1, 1), sampler.GetDiagnostics());
    }

    [Fact]
    public void Unsupported_tap_does_not_count_as_native_read()
    {
        var sampler = new SourcePeakSampler(new Levels(_ => 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.Sample(SourceKind.Bus, 0, MeterTap.PreFader));
        Assert.Equal(new MeterSamplingDiagnostics(0, 0), sampler.GetDiagnostics());
    }

    private sealed class Levels(Func<int, float> read) : IVoicemeeterLevels
    {
        public float GetLevel(int type, int channel) => read(channel);
    }
}
