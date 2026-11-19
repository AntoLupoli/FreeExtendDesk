using FreeExtend.Host.Display;
using FreeExtend.Host.Net;

namespace FreeExtend.Gui;

/// <summary>
/// The control window: requirement rows (what is installed, is the tablet connected), Start/Stop streaming, Settings.
/// All adb and process work runs off the UI thread; events from the host process are posted back to it.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly ProjectPaths _paths;
    private readonly SystemProbe _probe;
    private readonly FlowLayoutPanel _rows = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
    private readonly Button _start = new() { Size = new Size(436, 44), Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold), Margin = new Padding(0, 8, 0, 8) };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(436, 0), Margin = new Padding(0, 0, 0, 8) };
    private readonly Button _settings = new() { Text = "Settings", AutoSize = true, Anchor = AnchorStyles.Right };
    private readonly ToolTip _tip = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 3000 };

    private SystemSnapshot? _snapshot;
    private StreamSession? _session;
    private (int W, int H, int Hz)? _mode;
    private double _fps;
    private bool _busy, _streaming, _stopping, _closing, _polling;

    public MainForm(ProjectPaths paths)
    {
        _paths = paths;
        _probe = new SystemProbe(paths);

        Text = "FreeExtend";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(12) };
        layout.Controls.Add(_rows);
        layout.Controls.Add(_start);
        layout.Controls.Add(_status);
        layout.Controls.Add(_settings);
        Controls.Add(layout);

        _start.Click += async (_, _) => await OnStartStopClickedAsync();
        _settings.Click += async (_, _) => await OnSettingsClickedAsync();
        _poll.Tick += async (_, _) => await PollTabletAsync();
        UpdateControls();
        _start.Enabled = false;
        _status.Text = "Checking this PC...";
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshAllAsync();
        _poll.Start();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_streaming && !_closing && _session != null)
        {
            e.Cancel = true;                                    // keep the window until the host has stopped and the monitor is detached
            _closing = true;
            _status.Text = "Stopping...";
            _start.Enabled = false;
            await _session.StopAsync(StopTimeout);
            Close();
            return;
        }
        _poll.Stop();
        base.OnFormClosing(e);
    }

    // ---- refreshing ---------------------------------------------------------------------------------------------

    private async Task RefreshAllAsync(string? keepMessage = null)
    {
        _status.Text = keepMessage ?? "Checking this PC...";
        _snapshot = await _probe.CaptureAsync();
        Render(keepMessage);
    }

    private async Task PollTabletAsync()
    {
        if (_busy || _streaming || _polling || _snapshot == null) return;
        _polling = true;
        try
        {
            var (state, model) = await _probe.TabletOnlyAsync();
            if (_busy || _streaming || _snapshot == null) return;
            if (state != _snapshot.Tablet || model != _snapshot.TabletModel)
            {
                var full = await Task.Run(() => _probe.ProbeTablet(withApp: true));     // the tablet changed: look for the app too
                if (_busy || _streaming || _snapshot == null) return;
                _snapshot = _snapshot with { Tablet = full.State, TabletModel = full.Model, AppInstalled = full.AppInstalled };
                Render(null);
            }
        }
        finally { _polling = false; }
    }

    // ---- drawing ------------------------------------------------------------------------------------------------

    private void Render(string? message)
    {
        _rows.SuspendLayout();
        _rows.Controls.Clear();
        if (_snapshot != null)
            foreach (var r in StatusEvaluator.Evaluate(_snapshot)) _rows.Controls.Add(BuildRow(r));
        _rows.ResumeLayout();
        UpdateControls();
        if (message != null) _status.Text = message;
        else if (!_streaming && !_busy) _status.Text = _snapshot == null ? "" : StatusEvaluator.WhyCannotStart(_snapshot) ?? "Ready.";
    }

    private Control BuildRow(RequirementRow r)
    {
        var table = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, Width = 436, Margin = new Padding(0, 0, 0, 6) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var (glyph, color) = r.State switch
        {
            CheckState.Ok => ("✔", Color.SeaGreen),
            CheckState.Missing => ("✘", Color.Firebrick),
            CheckState.Warning => ("⚠", Color.DarkOrange),
            _ => ("•", Color.Gray),
        };
        table.Controls.Add(new Label { Text = glyph, ForeColor = color, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top }, 0, 0);
        table.Controls.Add(new Label { Text = r.Text, AutoSize = true, MaximumSize = new Size(310, 0), Anchor = AnchorStyles.Left | AnchorStyles.Top }, 1, 0);
        if (r.ActionLabel != null)
        {
            var button = new Button { Text = r.ActionLabel, AutoSize = true, Enabled = !_busy && !_streaming, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            button.Click += async (_, _) => await RunActionAsync(r.Id);
            table.Controls.Add(button, 2, 0);
        }
        return table;
    }

    private void UpdateControls()
    {
        string? why = _snapshot == null ? "Checking this PC..." : StatusEvaluator.WhyCannotStart(_snapshot);
        _start.Text = _streaming ? "Stop streaming" : "Start streaming";
        _start.Enabled = !_busy && !_stopping && (_streaming || why == null);
        _tip.SetToolTip(_start, _streaming ? "" : why ?? "");
        _settings.Enabled = !_busy;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        if (message != null) _status.Text = message;
        UpdateControls();
        foreach (Control row in _rows.Controls)
            foreach (Control c in row.Controls)
                if (c is Button b) b.Enabled = !busy && !_streaming;
    }

    private void Post(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { /* window already gone */ }
    }

    // ---- streaming ----------------------------------------------------------------------------------------------

    private async Task OnStartStopClickedAsync()
    {
        if (_streaming)
        {
            if (_session == null) return;
            _stopping = true;
            UpdateControls();
            _status.Text = "Stopping...";
            await _session.StopAsync(StopTimeout);              // the Exited event does the rest
            return;
        }
        StartStreaming();
    }

    private void StartStreaming()
    {
        var session = new StreamSession(_paths.HostExe);
        session.Status += s => Post(() => OnHostStatus(s));
        session.Exited += (code, err) => Post(() => OnHostExited(session, code, err));
        try { session.Start(); }
        catch (Exception ex)
        {
            session.Dispose();
            MessageBox.Show(this, "Could not start the host program:\n" + ex.Message, "FreeExtend", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _session = session;
        _streaming = true;
        _stopping = false;
        _mode = null;
        _fps = 0;
        SetBusy(false, "Starting...");
        foreach (Control row in _rows.Controls) foreach (Control c in row.Controls) if (c is Button b) b.Enabled = false;
    }

    private void OnHostStatus(HostStatus s)
    {
        if (!_streaming) return;
        switch (s.Kind)
        {
            case HostStatusKind.Waiting:
            case HostStatusKind.Disconnected:
                _mode = null;
                _status.Text = "Waiting for the tablet... (open the FreeExtend app on it)";
                break;
            case HostStatusKind.Connected:
                _mode = (s.Width, s.Height, s.Hz);
                _status.Text = $"Streaming {s.Width}x{s.Height}@{s.Hz}";
                break;
            case HostStatusKind.Fps when _mode is { } m:
                _fps = s.Fps;
                _status.Text = $"Streaming {m.W}x{m.H}@{m.Hz}, {_fps:0} fps";
                break;
        }
    }

    private async void OnHostExited(StreamSession session, int code, string? lastError)
    {
        if (!ReferenceEquals(session, _session)) return;
        _streaming = false;
        _stopping = false;
        _session = null;
        session.Dispose();
        if (_closing) { Close(); return; }
        await RefreshAllAsync(HostExitMessages.Describe(code, lastError));
    }

    // ---- actions ------------------------------------------------------------------------------------------------

    private async Task RunActionAsync(RequirementId id)
    {
        if (_busy || _streaming) return;
        SetBusy(true);
        try
        {
            switch (id)
            {
                case RequirementId.Driver:
                {
                    _status.Text = "Installing the driver (Windows will ask for permission)...";
                    // Always name the GPU: without it install.ps1 may wait for a keyboard answer in its hidden window.
                    string gpu = _snapshot?.Gpus.FirstOrDefault(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) ?? _snapshot?.Gpus.FirstOrDefault() ?? "";
                    var args = gpu.Length > 0 ? new[] { "-Gpu", gpu } : Array.Empty<string>();
                    Report(await ScriptRunner.RunAsync(_paths.InstallDriverScript, elevated: true, args), "Driver installation");
                    break;
                }
                case RequirementId.TabletApp:
                    _status.Text = "Installing the app on the tablet (this can take a minute)...";
                    Report(await ScriptRunner.RunAsync(_paths.InstallAppScript, elevated: false), "App installation");
                    break;
                case RequirementId.GpuPreference:
                    new GpuPreference().SetHighPerformance(_paths.HostExe);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FreeExtend", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
        await RefreshAllAsync();
    }

    private void Report(ScriptResult r, string what)
    {
        if (r.Cancelled) { _status.Text = what + " cancelled."; return; }
        if (r.ExitCode != 0)
            MessageBox.Show(this, $"{what} failed (code {r.ExitCode}).\n\n{r.LogTail}", "FreeExtend", MessageBoxButtons.OK, MessageBoxIcon.Error);
        else _status.Text = what + " finished.";
    }

    private async Task OnSettingsClickedAsync()
    {
        if (_busy) return;
        bool tabletReady = _snapshot?.Tablet == TabletState.Ready;
        using var form = new SettingsForm(tabletReady);
        if (form.ShowDialog(this) != DialogResult.Yes) return;

        if (_streaming && _session != null)
        {
            _stopping = true;
            UpdateControls();
            _status.Text = "Stopping...";
            await _session.StopAsync(StopTimeout);
            while (_streaming) await Task.Delay(100);           // wait for the Exited event to finish the cleanup
        }
        SetBusy(true, "Uninstalling (Windows will ask for permission)...");
        UninstallReport report;
        try { report = await Uninstaller.RunAsync(_paths, tabletReady); }
        finally { SetBusy(false); }

        string Line(string name, bool? ok) => ok switch { true => $"  ✔ {name}", false => $"  ✘ {name}: not removed", null => $"  - {name}: skipped (tablet not connected)" };
        string summary = "Uninstall finished:\n" +
            Line("Virtual display driver", report.DriverRemoved) + "\n" +
            Line("Graphics preference", report.GpuPreferenceRemoved) + "\n" +
            Line("App on the tablet", report.AppRemoved) +
            (report.Error != null ? "\n\n" + report.Error : "") +
            (report.DriverRemoved ? "\n\nRestart Windows if a ghost monitor remains." : "");
        MessageBox.Show(this, summary, "FreeExtend", MessageBoxButtons.OK, report.Error == null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await RefreshAllAsync();
    }
}
