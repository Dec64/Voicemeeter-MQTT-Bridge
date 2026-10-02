using Moq;

namespace VoicemeeterMqttBridge.Tests;

public class ReadOnlyMeterProbeTests
{
    [Fact]
    public void Unloaded_identity_reads_fail_instead_of_reporting_zero()
    {
        var remote = new VoicemeeterRemote(_ => { });
        Assert.Throws<InvalidOperationException>(() => remote.GetVoicemeeterType());
        Assert.Throws<InvalidOperationException>(() => remote.GetVoicemeeterVersion());
    }

    [Fact]
    public void Invalid_pass_count_never_loads_the_dll()
    {
        var remote = new Mock<IVoicemeeterRemote>(MockBehavior.Strict);
        foreach (int passes in new[] { 0, 201 })
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadOnlyMeterProbe.Capture(remote.Object,
                () => throw new Exception("Must not read identity"), passes));
    }

    [Fact]
    public void Failed_channel_remains_unavailable_without_contaminating_other_sources()
    {
        var remote = new Mock<IVoicemeeterRemote>(MockBehavior.Strict);
        remote.Setup(r => r.Load()); remote.Setup(r => r.Login()).Returns(0);
        remote.Setup(r => r.Logout()).Returns(0); remote.Setup(r => r.IsParametersDirty()).Returns(0);
        remote.Setup(r => r.GetLevel(It.IsAny<int>(), It.IsAny<int>())).Returns(0.5f);
        remote.Setup(r => r.GetLevel(0, 0)).Throws(new InvalidOperationException("No level"));
        var result = ReadOnlyMeterProbe.Capture(remote.Object, () => new(3, new Version(3, 1, 3, 0)), 1);
        var failed = Assert.Single(result.Frames[0].Peaks, p => !p.Available);
        Assert.Equal("strip:0", failed.SourceId); Assert.Equal(MeterTap.PreFader, failed.Tap);
        Assert.Null(failed.Dbfs); Assert.Null(failed.LinearPeak);
        remote.Verify(r => r.Logout(), Times.Once);
    }

    [Fact]
    public void Reads_all_canonical_taps_without_any_control_write_and_logs_out()
    {
        var remote = new Mock<IVoicemeeterRemote>(MockBehavior.Strict);
        remote.Setup(r => r.Load()); remote.Setup(r => r.Login()).Returns(0);
        remote.Setup(r => r.Logout()).Returns(0);
        remote.Setup(r => r.IsParametersDirty()).Returns(0);
        remote.Setup(r => r.GetLevel(It.IsAny<int>(), It.IsAny<int>())).Returns(0.5f);
        var result = ReadOnlyMeterProbe.Capture(remote.Object, () => new(3, new Version(3, 1, 3, 0)), 1);
        Assert.Single(result.Frames); Assert.Equal(24, result.Frames[0].Peaks.Count);
        Assert.All(result.Frames[0].Peaks, peak => Assert.Equal(-6.0206, peak.Dbfs!.Value, 4));
        remote.Verify(r => r.GetLevel(It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(132));
        remote.Verify(r => r.Logout(), Times.Once);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Absent_engine_never_launches_or_samples(int login)
    {
        var remote = new Mock<IVoicemeeterRemote>(MockBehavior.Strict);
        remote.Setup(r => r.Load()); remote.Setup(r => r.Login()).Returns(login);
        remote.Setup(r => r.Logout()).Returns(0);
        Assert.Throws<InvalidOperationException>(() => ReadOnlyMeterProbe.Capture(remote.Object,
            () => throw new Exception("Identity must not be called"), 1));
        remote.Verify(r => r.Logout(), login == 1 ? Times.Once() : Times.Never());
    }

    [Fact]
    public void Wrong_engine_and_failed_refresh_stop_before_levels_and_release_registration()
    {
        foreach (bool wrongEngine in new[] { true, false })
        {
            var remote = new Mock<IVoicemeeterRemote>(MockBehavior.Strict);
            remote.Setup(r => r.Load()); remote.Setup(r => r.Login()).Returns(0);
            remote.Setup(r => r.Logout()).Returns(0);
            remote.Setup(r => r.IsParametersDirty()).Returns(-2);
            Assert.ThrowsAny<InvalidOperationException>(() => ReadOnlyMeterProbe.Capture(remote.Object,
                () => new(wrongEngine ? 2 : 3, new Version(3, 1, 3, 0)), 1));
            remote.Verify(r => r.GetLevel(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            remote.Verify(r => r.Logout(), Times.Once);
        }
    }
}
