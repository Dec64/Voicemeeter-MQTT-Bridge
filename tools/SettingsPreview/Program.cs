using System.Drawing.Imaging;
using VoicemeeterMqttBridge;
internal static class Preview
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: SettingsPreview OUTPUT_DIRECTORY"); return 2; }
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(args[0]);
        var settings = new AppSettings { StartPotatoWithApp = false };
        settings.MeteringV2.LegacyMetersIntervalMs = 50;
        // No StartAsync, AppSettings.Load/Save, native load, MQTT connect or audio writes.
        var bridge = new BridgeService(settings, new VoicemeeterRemote(_ => { }), log: _ => { });
        using var form = new MeteringSettingsForm(settings, bridge);
        form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000); form.Show(); Application.DoEvents();
        foreach (var size in new[] { new Size(1000,800), new Size(850,650) })
        {
            form.Size = size; form.PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(args[0], $"metering-settings-{size.Width}x{size.Height}.png"), ImageFormat.Png);
        }
        // Exercise the actual UI capture path; opening/applying must not clamp a valid legacy cadence.
        var layout = form.Controls.OfType<TableLayoutPanel>().Single();
        var apply = layout.Controls.OfType<FlowLayoutPanel>().SelectMany(panel => panel.Controls.OfType<Button>())
            .Single(button => button.Text == "Apply to settings");
        apply.PerformClick();
        if (form.Result?.LegacyMetersIntervalMs != 50) throw new InvalidOperationException("Settings UI changed the legacy cadence.");
        bridge.StopAsync().GetAwaiter().GetResult();
        return 0;
    }
}
