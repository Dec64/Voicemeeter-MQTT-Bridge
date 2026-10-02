using System.Text;

namespace VoicemeeterMqttBridge.Tests;

public class DevelopmentTimingTests
{
    [Fact]
    public void Receipt_window_includes_idle_time_and_counts_only_accepted_payloads()
    {
        var clock = new ManualTimeProvider();
        var observation = new DevelopmentStreamObservation("test", clock);
        void Send(string suffix, string json) => observation.Observe("test/v2/" + suffix, Encoding.UTF8.GetBytes(json), false);
        const string metadata = """{"schema":2,"session_id":"one"}""";
        const string frame = """{"schema":2,"session_id":"one","seq":0}""";
        Send("metadata", metadata);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Send("meters/fast", frame);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        Send("meters/fast", frame.Replace(":0", ":1"));
        Send("meters/fast", frame); // rejected duplicate does not inflate bytes/rate
        clock.Advance(TimeSpan.FromMilliseconds(850));
        var result = observation.TimingSnapshot();
        Assert.Equal(1000, result.ElapsedMs);
        Assert.Equal(2, result.Fast.AcceptedFrames);
        Assert.Equal(2 * Encoding.UTF8.GetByteCount(frame), result.Fast.PayloadBytes);
        Assert.Equal(2, result.Fast.FramesPerSecond);
        Assert.Equal(50, result.Fast.MeanSpacingMs);
        Assert.Equal(50, result.Fast.MaxSpacingMs);
        Assert.Equal(0, result.Slow.AcceptedFrames);
        Assert.Null(result.Slow.MeanSpacingMs);
    }

    [Fact]
    public void Empty_window_and_single_frame_have_no_spacing_or_zero_duration_rate()
    {
        var observation = new DevelopmentStreamObservation("test", new ManualTimeProvider());
        observation.Observe("other", Encoding.UTF8.GetBytes("malformed"), false);
        var result = observation.TimingSnapshot();
        Assert.Null(result.Fast.FramesPerSecond);
        Assert.Null(result.Fast.MaxSpacingMs);
        Assert.Equal(0, result.Fast.PayloadBytes);
        observation.Observe("test/v2/metadata", Encoding.UTF8.GetBytes("""{"schema":2,"session_id":"one"}"""), false);
        observation.Observe("test/v2/meters/fast", Encoding.UTF8.GetBytes("""{"schema":2,"session_id":"one","seq":0}"""), false);
        result = observation.TimingSnapshot();
        Assert.Equal(1, result.Fast.AcceptedFrames);
        Assert.Null(result.Fast.FramesPerSecond);
        Assert.Null(result.Fast.MeanSpacingMs);
    }
}
