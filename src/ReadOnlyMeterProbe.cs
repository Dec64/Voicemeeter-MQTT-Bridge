// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
namespace VoicemeeterMqttBridge;

public sealed record ProbeFrame(DateTimeOffset CapturedAtUtc, IReadOnlyList<SourcePeak> Peaks);
public sealed record MeterProbeReport(EngineIdentity Engine, IReadOnlyList<ProbeFrame> Frames);

/// <summary>Bounded synchronous diagnostic: one calling thread, no settings, setters or MQTT.</summary>
public static class ReadOnlyMeterProbe
{
    public static MeterProbeReport Capture(IVoicemeeterRemote remote, Func<EngineIdentity> readIdentity, int passes = 20)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(readIdentity);
        if (passes is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(passes));
        remote.Load();
        int login = remote.Login();
        if (login is not (0 or 1)) throw new InvalidOperationException($"Probe login failed: {login}.");
        try
        {
            if (login == 1) throw new InvalidOperationException("Voicemeeter is not running; the probe will not start it.");
            var engine = readIdentity();
            if (engine.Type != 3 || engine.Version is null || engine.Version.Major != 3 || engine.Version < new Version(3, 1, 0, 1))
                throw new InvalidOperationException("Probe requires a reported Potato 3.x version at least 3.1.0.1.");
            var sampler = new SourcePeakSampler(remote);
            var frames = new List<ProbeFrame>();
            for (int pass = 0; pass < passes; pass++)
            {
                if (pass > 0) Thread.Sleep(50); // Diagnostic pacing, not a throughput benchmark.
                if (readIdentity() != engine) throw new InvalidOperationException("Engine identity changed during the probe.");
                int dirty = remote.IsParametersDirty();
                if (dirty < 0) throw new InvalidOperationException($"Probe refresh failed: {dirty}.");
                var peaks = new List<SourcePeak>();
                foreach (var source in PotatoChannelMap.All)
                    foreach (var tap in source.Kind == SourceKind.Bus
                        ? new[] { MeterTap.Output } : new[] { MeterTap.PreFader, MeterTap.PostMute })
                        peaks.Add(sampler.Sample(source.Kind, source.Index, tap));
                frames.Add(new(DateTimeOffset.UtcNow, peaks.AsReadOnly()));
            }
            return new(engine, frames.AsReadOnly());
        }
        finally
        {
            int logout = remote.Logout();
            if (logout != 0) throw new InvalidOperationException($"Probe logout failed: {logout}.");
        }
    }
}
