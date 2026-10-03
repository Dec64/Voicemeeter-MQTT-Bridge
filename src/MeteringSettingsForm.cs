// Voicemeeter MQTT Bridge. See LICENSE and upstream attribution.
using System.Text.Json;
namespace VoicemeeterMqttBridge;
public sealed class MeteringSettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly BridgeService _bridge;
    private MeteringV2Settings _draft;
    private readonly CheckBox _enabled = new() { Text = "Enable v2 telemetry", AutoSize = true };
    private readonly CheckBox _fast = new() { Text = "Fast MQTT stream", AutoSize = true };
    private readonly CheckBox _slow = new() { Text = "Slow HA sensors", AutoSize = true };
    private readonly CheckBox _legacy = new() { Text = "Legacy raw meters", AutoSize = true };
    private readonly NumericUpDown _sample = Number(10, 1000), _fastMs = Number(50, 5000), _slowMs = Number(250, 60000), _legacyMs = Number(1, int.MaxValue);
    private readonly DataGridView _sources = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
    private readonly CheckedListBox _groups = new() { Height = 100, Width = 260, CheckOnClick = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(840, 0) };
    private readonly TextBox _diagnostics = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    public MeteringV2Settings? Result { get; private set; }
    public MeteringSettingsForm(AppSettings settings, BridgeService bridge)
    {
        _settings = settings; _bridge = bridge; _draft = SettingsEditor.CloneMetering(settings.MeteringV2);
        Text = "Advanced metering and source mapping"; Width = 1000; Height = 800;
        MinimumSize = new Size(850, 650); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(16) };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 65)); layout.RowStyles.Add(new(SizeType.Percent, 35)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var flags = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        flags.Controls.AddRange([_enabled, _fast, _slow, _legacy]); layout.Controls.Add(flags, 0, 0);
        var rates = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        foreach (var item in new[] { ("Sample ms", _sample), ("Fast ms", _fastMs), ("Slow ms", _slowMs), ("Legacy ms", _legacyMs) })
        { rates.Controls.Add(new Label { Text = item.Item1, AutoSize = true, Padding = new Padding(0, 8, 0, 0) }); rates.Controls.Add(item.Item2); }
        _groups.Items.AddRange(AdvancedControlRegistry.Groups); rates.Controls.Add(new Label { Text = "Advanced discovery (EQ cells can add 240 entities per source)", Width = 270, Height = 70 }); rates.Controls.Add(_groups);
        layout.Controls.Add(rates, 0, 1);
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "Canonical source", ReadOnly = true });
        _sources.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "Enabled" });
        _sources.Columns.Add("label", "Display label"); _sources.Columns.Add("alias", "Unique alias");
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "engine", HeaderText = "Engine label", ReadOnly = true });
        _sources.Columns.Add(new DataGridViewTextBoxColumn { Name = "taps", HeaderText = "Meter taps", ReadOnly = true });
        _sources.Columns.Add(new DataGridViewButtonColumn { Name = "test", HeaderText = "Local peak", Text = "Test source", UseColumnTextForButtonValue = true });
        _sources.CellContentClick += async (_, e) => {
            if (e.RowIndex < 0 || e.ColumnIndex != _sources.Columns["test"].Index) return;
            string id = (string)_sources.Rows[e.RowIndex].Cells["id"].Value;
            try { var result = await _bridge.TestSourceAsync(id); _status.Text = string.Join(" | ", result.Select(p => MeterTapNames.Format(p.Tap) + ": " + (p.Available ? p.Dbfs!.Value.ToString("F1") + " dBFS" : "unavailable"))); }
            catch (Exception ex) { _status.Text = "Source test unavailable: " + ex.GetType().Name; }
        };
        layout.Controls.Add(_sources, 0, 2); layout.Controls.Add(_diagnostics, 0, 3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        footer.Controls.Add(_status);
        AddButton(footer, "Read engine labels", async () => {
            try { var registry = await _bridge.ReadSourcesAsync(_draft); foreach (DataGridViewRow row in _sources.Rows) row.Cells["engine"].Value = registry.Sources.Single(s => s.Id == (string)row.Cells["id"].Value).EngineLabel; _status.Text = "Detected " + registry.Engine.Version + ". Labels do not prove physical audio routing."; }
            catch (Exception ex) { _status.Text = "Engine unavailable: " + ex.GetType().Name; }
        });
        AddButton(footer, "Export metering", () => { CaptureDraft(); using var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "metering-v2.json" }; if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, SettingsEditor.ExportMetering(_draft)); return Task.CompletedTask; });
        AddButton(footer, "Reset v2 only", () => { _draft = new() { LegacyMetersEnabled = _settings.PublishMeters, LegacyMetersIntervalMs = _settings.PublishMetersEveryMs }; LoadDraft(); return Task.CompletedTask; });
        AddButton(footer, "Apply to settings", () => { CaptureDraft(); Result = _draft; DialogResult = DialogResult.OK; Close(); return Task.CompletedTask; });
        AddButton(footer, "Cancel", () => { Close(); return Task.CompletedTask; });
        layout.Controls.Add(footer, 0, 4); Controls.Add(layout); LoadDraft();
        _timer.Tick += (_, _) => _diagnostics.Text = "MQTT: " + _bridge.MqttStatus + Environment.NewLine + "Voicemeeter: " + _bridge.VoicemeeterStatus + Environment.NewLine + "Telemetry: " + _bridge.TelemetryStatus + Environment.NewLine + _bridge.TelemetryDiagnostics;
        _timer.Start();
    }
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 100 };
    private void AddButton(Control parent, string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 32) };
        button.Click += async (_, _) => { button.Enabled = false; try { await action(); } catch (Exception ex) { _status.Text = ex is ArgumentException ? ex.Message : "Operation failed: " + ex.GetType().Name; } finally { if (!button.IsDisposed) button.Enabled = true; } };
        parent.Controls.Add(button);
    }
    private void LoadDraft()
    {
        for (int i=0;i<_groups.Items.Count;i++) _groups.SetItemChecked(i,_draft.AdvancedDiscoveryGroups.Contains((string)_groups.Items[i]));
        _enabled.Checked = _draft.Enabled; _fast.Checked = _draft.FastEnabled; _slow.Checked = _draft.SlowEnabled; _legacy.Checked = _draft.LegacyMetersEnabled;
        _sample.Value = Math.Clamp(_draft.SampleIntervalMs, 10, 1000); _fastMs.Value = Math.Clamp(_draft.FastPublishIntervalMs, 50, 5000); _slowMs.Value = Math.Clamp(_draft.SlowPublishIntervalMs, 250, 60000); _legacyMs.Value = Math.Clamp(_draft.LegacyMetersIntervalMs, 1, int.MaxValue);
        _sources.Rows.Clear(); foreach (var map in PotatoChannelMap.All) { var source = _draft.Sources.SingleOrDefault(s => s.Id == map.Id); _sources.Rows.Add(map.Id, source?.Enabled ?? false, source?.DisplayLabel ?? "", source?.Alias ?? "", "", map.Kind == SourceKind.Bus ? "Output" : "Incoming / after mute"); }
        _status.Text = "Select verified sources. Incoming and post-mute taps are independent. Save in the main dialog, then restart the bridge to apply v2 changes.";
    }
    private void CaptureDraft()
    {
        _sources.EndEdit(); var next = SettingsEditor.CloneMetering(_draft);
        next.AdvancedDiscoveryGroups = _groups.CheckedItems.Cast<string>().ToList();
        next.Enabled = _enabled.Checked; next.FastEnabled = _fast.Checked; next.SlowEnabled = _slow.Checked; next.LegacyMetersEnabled = _legacy.Checked;
        next.SampleIntervalMs = (int)_sample.Value; next.FastPublishIntervalMs = (int)_fastMs.Value; next.SlowPublishIntervalMs = (int)_slowMs.Value; next.LegacyMetersIntervalMs = (int)_legacyMs.Value;
        next.Sources = _sources.Rows.Cast<DataGridViewRow>().Select(row => {
            string id = (string)row.Cells["id"].Value; var original = next.Sources.SingleOrDefault(s => s.Id == id) ?? new SourceProfile { Id = id };
            original.Enabled = row.Cells["enabled"].Value is true; original.DisplayLabel = row.Cells["label"].Value?.ToString()?.Trim(); original.Alias = row.Cells["alias"].Value?.ToString()?.Trim(); return original;
        }).ToList(); next.Validate(); _draft = next;
    }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
