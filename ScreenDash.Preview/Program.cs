using System.Drawing.Imaging;
using System.Reflection;
using ScreenDash;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "work/ui-preview");
        Directory.CreateDirectory(output);
        using var controller = new PreviewController();
        using var form = new MainForm(controller, enableHotkeys: false);
        form.Text = "ScreenDash 1.3 · Vorschau mit Beispieldaten";
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.ClientSize = new Size(1180, 780);
        form.Show();
        var showPage = typeof(MainForm).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var refreshDashboard = typeof(MainForm).GetMethod("RefreshDashboard", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string[] names = ["01-Aufnahme", "02-Einstellungen", "03-Aufnahmen"];
        for (int page = 0; page < names.Length; page++)
        {
            controller.Recording = page == 0;
            refreshDashboard.Invoke(form, null);
            showPage.Invoke(form, [page]);
            if (page == 2)
            {
                var recordings = (DataGridView)typeof(MainForm).GetField("_recordings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                recordings.Rows[1].Selected = true;
            }
            form.PerformLayout();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            string file = Path.Combine(output, names[page] + ".png");
            bitmap.Save(file, ImageFormat.Png);
            Console.WriteLine(file);
        }
        var settingTab = typeof(MainForm).GetMethod("ShowSettingTab", BindingFlags.NonPublic | BindingFlags.Instance)!;
        showPage.Invoke(form, [1]);
        for (int tab = 1; tab < 4; tab++)
        {
            settingTab.Invoke(form, [tab]); form.PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, $"04-Einstellungen-{tab}.png"), ImageFormat.Png);
        }
        form.ClientSize = new Size(1024, 720);
        for (int page = 0; page < 3; page++)
        {
            controller.Recording = page == 0; refreshDashboard.Invoke(form, null); showPage.Invoke(form, [page]);
            if (page == 1) settingTab.Invoke(form, [2]);
            form.PerformLayout(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, $"05-Kompakt-{page}.png"), ImageFormat.Png);
            if (page == 0)
            {
                var dashboard = (Panel)typeof(MainForm).GetField("_dashboard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                Console.WriteLine($"Compact dashboard: {dashboard.ClientSize}, grid {dashboard.Controls[0].Size}, DPI {form.DeviceDpi}, scroll {dashboard.DisplayRectangle}");
            }
        }
        RunUiChecks(form, controller);
        var clipDialogType = typeof(MainForm).GetNestedType("ClipDialog", BindingFlags.NonPublic)!;
        using var clipDialog = (Form)Activator.CreateInstance(clipDialogType, ["ScreenDash_Beispiel.mkv", new MediaDetails(TimeSpan.FromMinutes(8.2), 2560, 1440, true)])!;
        clipDialog.StartPosition = FormStartPosition.Manual; clipDialog.Location = new Point(-20000, -20000); clipDialog.Show(); clipDialog.PerformLayout(); Application.DoEvents();
        using var clipBitmap = new Bitmap(clipDialog.Width, clipDialog.Height); clipDialog.DrawToBitmap(clipBitmap, new Rectangle(Point.Empty, clipDialog.Size)); clipBitmap.Save(Path.Combine(output, "06-Clip.png"), ImageFormat.Png);
    }
    private static void RunUiChecks(MainForm form, PreviewController controller)
    {
        int checks = 0;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        T Field<T>(string name) where T : class => (T)typeof(MainForm).GetField(name, flags)!.GetValue(form)!;
        object? Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, flags)!.Invoke(form, args);
        void Check(bool result, string description) { if (!result) throw new InvalidOperationException("UI check failed: " + description); checks++; }
        controller.Recording = false; Call("ShowPage", 2);
        var grid = Field<DataGridView>("_recordings");
        grid.ClearSelection(); grid.Rows[0].Selected = true; grid.Rows[1].Selected = true;
        Call("RefreshRecordings", true);
        Check(grid.SelectedRows.Count == 2, "multi selection survives archive refresh");
        var existingRow = grid.Rows[0]; Call("RefreshRecordings", true);
        Check(ReferenceEquals(existingRow, grid.Rows[0]), "unchanged refresh reuses rows instead of rebuilding the table");
        Call("SortArchive", "Size");
        Check(((RecordingInfo)grid.Rows[0].Tag!).Bytes >= ((RecordingInfo)grid.Rows[1].Tag!).Bytes, "size sorting uses bytes, not formatted text");
        Call("SortArchive", "Time");
        int scans = controller.RecordingScans;
        var search = Field<TextBox>("_search"); search.Text = "wichtiger";
        Check(grid.Rows.Count == 1 && grid.Rows[0].Cells[1].Value!.ToString()!.Contains("Demo"), "search matches note");
        Check(controller.RecordingScans == scans, "search uses cached list"); search.Clear();
        var filter = Field<ComboBox>("_archiveFilter"); filter.SelectedIndex = 1;
        Check(grid.Rows.Count == 2, "protected filter"); filter.SelectedIndex = 2;
        Check(grid.Rows.Count == 2, "unprotected filter"); filter.SelectedIndex = 0;
        var date = Field<ComboBox>("_dateFilter"); date.SelectedIndex = 1;
        Check(grid.Rows.Count == 3, "last 24 hours filter"); date.SelectedIndex = 0;
        Check(controller.RecordingScans == scans, "all filters use cached list");
        Call("ShowPage", 1);
        var profile = Field<ComboBox>("_profile"); profile.SelectedIndex = 0;
        var settings = (AppSettings)Call("ReadSettings")!;
        Check(settings.Fps == 10 && settings.VideoBitrateKbps == 1500, "ultra sparing preset");
        Field<NumericUpDown>("_customFps").Value = 47; Field<NumericUpDown>("_customBitrate").Value = 8123;
        settings = (AppSettings)Call("ReadSettings")!;
        Check(settings.Fps == 47 && settings.VideoBitrateKbps == 8123 && profile.SelectedIndex == 4, "custom quality round trip");
        Call("LoadSettings"); controller.Paused = true; Call("RefreshDashboard");
        Check(Field<Button>("_pause").Enabled && Field<Button>("_pause").Text.Contains("Fortsetzen"), "paused state exposes resume");
        controller.Paused = false; Call("RefreshDashboard");
        Check(!Field<Button>("_pause").Enabled, "idle state disables pause");
        Call("LoadSettings"); Call("RefreshDashboard");
        Check(!Field<Button>("_save").Enabled, "unchanged settings do not offer a redundant save");
        Field<NumericUpDown>("_customFps").Value = 21;
        Check(Field<Button>("_save").Enabled && Field<Label>("_settingsHint").Text.Contains("Ungespeicherte"), "unsaved settings have a visible state");
        Call("LoadSettings");
        Check(Field<CheckBox>("_singleFile").Checked && !Field<NumericUpDown>("_segment").Enabled, "continuous file is the default and disables segment length");
        typeof(MainForm).GetField("_exporting", flags)!.SetValue(form, true); controller.Recording = true; Call("RefreshDashboard");
        Check(Field<Button>("_toggle").Enabled && Field<Button>("_pause").Enabled && !Field<Button>("_export").Enabled, "export leaves recording controls available but prevents another export");
        typeof(MainForm).GetField("_exporting", flags)!.SetValue(form, false); controller.Recording = false;
        Call("ShowPage", 2); controller.DelayNextScan = true; Call("RefreshRecordings", true);
        Check((bool)typeof(MainForm).GetField("_archiveLoading", flags)!.GetValue(form)!, "a slow archive is loaded asynchronously");
        search.Text = "wichtiger";
        Check(grid.Rows.Count == 1, "cached filtering remains available during background load");
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((bool)typeof(MainForm).GetField("_archiveLoading", flags)!.GetValue(form)! && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(!(bool)typeof(MainForm).GetField("_archiveLoading", flags)!.GetValue(form)!, "the background reload completes without freezing the UI");
        search.Clear();
        controller.ArchiveOverride = Enumerable.Range(0, 1201).Select(i => new RecordingInfo($"record-{i:0000}.mkv", DateTime.UtcNow.AddMinutes(-i), i * 1024L, i % 3 == 0, false)).ToArray();
        Call("RefreshRecordings", true);
        Check(grid.Rows.Count == 200 && Field<Label>("_recordingsSummary").Text.StartsWith("1201 von 1201"), "large archives show 200 rows and the complete match count");
        Check(Field<Label>("_archivePageLabel").Text.Contains("1–200 von 1201") && !Field<Button>("_previousArchivePage").Enabled && Field<Button>("_nextArchivePage").Enabled, "first-page boundaries are clear");
        scans = controller.RecordingScans; Call("ChangeArchivePage", 1);
        Check(grid.Rows.Count == 200 && Field<Label>("_archivePageLabel").Text.Contains("201–400 von 1201"), "next page shows the second 200 records");
        Check(controller.RecordingScans == scans, "paging never rescans disk");
        grid.ClearSelection(); grid.Rows[4].Selected = true; grid.Rows[6].Selected = true; var selectedPaths = grid.SelectedRows.Cast<DataGridViewRow>().Select(row => ((RecordingInfo)row.Tag!).Path).OrderBy(path => path).ToArray();
        Call("RefreshRecordings", true);
        Check(grid.SelectedRows.Count == 2 && selectedPaths.SequenceEqual(grid.SelectedRows.Cast<DataGridViewRow>().Select(row => ((RecordingInfo)row.Tag!).Path).OrderBy(path => path)), "visible-page selection survives background reload");
        Call("ChangeArchivePage", 5);
        Check(grid.Rows.Count == 1 && Field<Label>("_archivePageLabel").Text.Contains("1201–1201") && !Field<Button>("_nextArchivePage").Enabled, "last page uses the remainder and disables next");
        Check(grid.SelectedRows.Count == 0, "paging does not keep an invisible selection");
        search.Text = "record-000";
        Check(grid.Rows.Count == 10 && Field<Label>("_archivePageLabel").Text.StartsWith("Seite 1 von 1"), "search resets pagination and displays filtered totals");
        search.Clear(); Call("ChangeArchivePage", 1); Call("SortArchive", "Size");
        Check(Field<Label>("_archivePageLabel").Text.StartsWith("Seite 1"), "sorting resets pagination");
        Call("ChangeArchivePage", 1); filter.SelectedIndex = 1;
        Check(Field<Label>("_archivePageLabel").Text.StartsWith("Seite 1"), "protection filter resets pagination");
        filter.SelectedIndex = 0; Call("ChangeArchivePage", 1); date.SelectedIndex = 1;
        Check(Field<Label>("_archivePageLabel").Text.StartsWith("Seite 1"), "date filter resets pagination");
        date.SelectedIndex = 0; Call("ChangeArchivePage", 6);
        controller.ArchiveOverride = controller.ArchiveOverride.Take(190).ToArray(); Call("RefreshRecordings", true);
        Check(grid.Rows.Count == 190 && Field<Label>("_archivePageLabel").Text.StartsWith("Seite 1 von 1") && !Field<Button>("_nextArchivePage").Enabled, "deleting records clamps a removed last page");
        grid.ClearSelection(); grid.Rows[0].Selected = true; Call("UpdateRecordingActions");
        Check(Field<Button>("_clip").Enabled, "a single completed recording enables clip export");
        controller.Recording = true;
        Func<CancellationToken, Task> heldExport = token => Task.Delay(Timeout.Infinite, token);
        var heldExportTask = (Task)Call("RunArchiveExportAsync", heldExport, "Prüfexport", (Func<string>)(() => "Test"))!;
        Call("RefreshDashboard");
        Check(Field<Button>("_cancelExport").Visible && Field<Button>("_toggle").Enabled && Field<Button>("_pause").Enabled && !Field<Button>("_clip").Enabled, "an actual pending export keeps capture controls usable and exposes cancellation");
        Call("CancelExport"); deadline = DateTime.UtcNow.AddSeconds(2);
        while (!heldExportTask.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(heldExportTask.IsCompletedSuccessfully && !Field<Button>("_cancelExport").Visible && Field<Button>("_clip").Enabled, "cancelled export clears its busy state and re-enables tools");
        controller.Recording = false;
        grid.Rows[1].Selected = true; Call("UpdateRecordingActions");
        Check(!Field<Button>("_clip").Enabled, "multiple selected recordings disable clip export");
        controller.ArchiveOverride = [new RecordingInfo("active.mkv", DateTime.UtcNow, 1024, false, true)]; Call("RefreshRecordings", true); grid.Rows[0].Selected = true; Call("UpdateRecordingActions");
        Check(!Field<Button>("_clip").Enabled, "an active recording cannot be clipped");
        Call("ShowPage", 1); controller.Settings.AutoStopMinutes = 0; Call("LoadSettings");
        Check(!Field<CheckBox>("_autoStopEnabled").Checked && !Field<NumericUpDown>("_autoStopMinutes").Enabled && ((AppSettings)Call("ReadSettings")!).AutoStopMinutes == 0, "auto stop remains disabled by default");
        Field<CheckBox>("_autoStopEnabled").Checked = true; Field<NumericUpDown>("_autoStopMinutes").Value = 17;
        Check(Field<NumericUpDown>("_autoStopMinutes").Enabled && ((AppSettings)Call("ReadSettings")!).AutoStopMinutes == 17, "auto stop setting round trips");
        Field<ComboBox>("_display").SelectedIndex = 1; Field<ComboBox>("_audio").SelectedIndex = 1; Call("LoadDevices", true);
        var sourceDraft = (AppSettings)Call("ReadSettings")!;
        Check(sourceDraft.DisplayId == "second" && sourceDraft.AudioDeviceId == "headset", "device refresh preserves the user's current unsaved source selection");
        controller.Recording = true; controller.Remaining = TimeSpan.FromSeconds(47); Call("RefreshDashboard");
        Check(Field<Label>("_autoStopCountdown").Text.Contains("00:00:47"), "dashboard shows auto stop countdown");
        controller.Recording = false; controller.Paused = true; Call("RefreshDashboard");
        Check(Field<Label>("_autoStopCountdown").Text.Contains("Pausiert"), "paused countdown is visibly paused");
        var clipDialogType = typeof(MainForm).GetNestedType("ClipDialog", BindingFlags.NonPublic)!;
        using var clipDialog = (Form)Activator.CreateInstance(clipDialogType, ["Beispiel.mkv", new MediaDetails(TimeSpan.FromSeconds(135), 1920, 1080, true)])!;
        clipDialog.StartPosition = FormStartPosition.Manual; clipDialog.Location = new Point(-20000, -20000); clipDialog.Show(); Application.DoEvents();
        T ClipField<T>(string name) where T : class => (T)clipDialogType.GetField(name, flags)!.GetValue(clipDialog)!;
        ClipRange Range() => (ClipRange)clipDialogType.GetProperty("SelectedRange")!.GetValue(clipDialog)!;
        Check(Range().Start == TimeSpan.FromSeconds(105) && Range().Duration == TimeSpan.FromSeconds(30), "clip defaults to the last 30 seconds");
        clipDialog.Controls.Find("Last300", true).OfType<Button>().Single().PerformClick();
        Check(Range().Start == TimeSpan.Zero && Range().Duration == TimeSpan.FromSeconds(135), "clip presets clamp to the recording length");
        ClipField<NumericUpDown>("_start").Value = 130; ClipField<NumericUpDown>("_duration").Value = 10;
        Check(!ClipField<Button>("_accept").Enabled, "out-of-bounds clip length cannot be accepted");
        ClipField<NumericUpDown>("_duration").Value = 5;
        Check(ClipField<Button>("_accept").Enabled, "a clip ending exactly at the recording boundary is accepted");
        clipDialog.Hide(); controller.Paused = false; controller.Recording = false; controller.DelayNextStart = true; controller.HoldStop = true; Call("ShowPage", 0);
        var delayedStart = (Task)Call("ToggleRecordingActionAsync")!; Call("RefreshDashboard");
        Check(!delayedStart.IsCompleted && Field<Button>("_toggle").Enabled && Field<Button>("_toggle").Text.Contains("stoppen") && Field<ToolStripMenuItem>("_trayToggle").Enabled, "stop button and tray remain usable while an encoder start is pending");
        Check(!Field<Button>("_pause").Enabled && !Field<Button>("_protect").Enabled, "other capture actions stay disabled during startup");
        var delayedStop = (Task)Call("ToggleRecordingActionAsync")!;
        Check(controller.StopRequests == 1 && !delayedStop.IsCompleted && !delayedStart.IsCompleted, "a stop request reaches the controller before delayed start completes");
        Check(!Field<Button>("_toggle").Enabled && Field<Button>("_toggle").Text.Contains("gestoppt"), "pending stop has a distinct disabled state");
        Call("ToggleRecordingActionAsync");
        Check(controller.StopRequests == 1 && controller.StartRequests == 1, "repeated toggle cannot issue another stop or start while stopping");
        controller.CompleteHeldStop(); deadline = DateTime.UtcNow.AddSeconds(2);
        while ((!delayedStart.IsCompleted || !delayedStop.IsCompleted) && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(delayedStart.IsCompletedSuccessfully && delayedStop.IsCompletedSuccessfully && Field<Button>("_toggle").Enabled && Field<Button>("_toggle").Text.Contains("starten"), "cancelled startup returns to idle without an error dialog");
        Console.WriteLine($"UI checks passed: {checks}");
    }
}

internal sealed class PreviewController : IAppController
{
    public bool Recording { get; set; }
    public bool Paused { get; set; }
    public int RecordingScans { get; private set; }
    public bool DelayNextScan { get; set; }
    public bool DelayNextStart { get; set; }
    public bool HoldStop { get; set; }
    public bool StartPending { get; private set; }
    public bool StopPending { get; private set; }
    public int StartRequests { get; private set; }
    public int StopRequests { get; private set; }
    private bool _startCanceled;
    private TaskCompletionSource? _heldStart, _heldStop;
    public IReadOnlyList<RecordingInfo>? ArchiveOverride { get; set; }
    public TimeSpan? Remaining { get; set; } = TimeSpan.FromMinutes(42.6);
    public AppSettings Settings { get; } = new() { DisplayId = "test", AudioDeviceId = "speakers", AutoStopMinutes = 60 };
    public IReadOnlyList<DisplaySource> Displays { get; } = [new("test", "Bildschirm 1 · 2560 × 1440", 0, 0, 0, 0, 2560, 1440), new("second", "Bildschirm 2 · 1920 × 1080", 0, 1, 2560, 0, 1920, 1080)];
    public IReadOnlyList<AudioSource> AudioDevices { get; } = [new("speakers", "Lautsprecher · Realtek Audio"), new("headset", "Headset · Spielausgabe")];
    public DashboardStatus Status => new(Recording, StartPending || StopPending, Recording ? "Bildschirm 1 wird im Hintergrund aufgenommen. Du kannst ScreenDash minimieren." : "Bereit für die erste Aufnahme", "NVIDIA NVENC", TimeSpan.FromMinutes(17.4), Recording ? 1.7 : 0.2, 83 * 1024L * 1024, 15,
        new(0, 0, 6L * 1024 * 1024 * 1024, 512L * 1024 * 1024, 185L * 1024 * 1024 * 1024, true, ""), Armed: Recording || Paused || StartPending && !_startCanceled, Paused: Paused, PauseReason: Paused ? "Manuell pausiert" : "", AudioPeak: Recording ? 0.26 : 0, AudioDevice: "Headset · Spielausgabe", Remaining: Recording || Paused ? Remaining : null);
    public event Action? Changed;
    public event Action<string>? Notice { add { } remove { } }
    public async Task ToggleRecordingAsync()
    {
        StartRequests++;
        if (!DelayNextStart) return;
        DelayNextStart = false; StartPending = true; _startCanceled = false; _heldStart = new(TaskCreationOptions.RunContinuationsAsynchronously); Changed?.Invoke();
        try { await _heldStart.Task; }
        finally { StartPending = false; Changed?.Invoke(); }
    }
    public async Task StopAsync()
    {
        StopRequests++; _startCanceled = true; Recording = Paused = false; StopPending = true; Changed?.Invoke();
        try { if (HoldStop) { _heldStop = new(TaskCreationOptions.RunContinuationsAsynchronously); await _heldStop.Task; } _heldStart?.TrySetCanceled(); }
        finally { StopPending = false; Changed?.Invoke(); }
    }
    public void CompleteHeldStop() { HoldStop = false; _heldStop?.TrySetResult(); }
    public Task PauseAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
    public Task SaveSettingsAsync(AppSettings settings) => Task.CompletedTask;
    public Task RefreshDevicesAsync() => Task.CompletedTask;
    public Task ProtectRecentAsync() => Task.CompletedTask;
    public IReadOnlyList<RecordingInfo> GetRecordings()
    {
        RecordingScans++;
        if (ArchiveOverride is { } records) return records;
        return [
        new("ScreenDash_20261013_181500_001.mkv", DateTime.UtcNow.AddMinutes(-5), 112L * 1024 * 1024, false, true),
        new("ScreenDash_20261013_181000_000.mkv", DateTime.UtcNow.AddMinutes(-10), 109L * 1024 * 1024, true, false, "Wichtiger Moment · Demo"),
        new("ScreenDash_20261013_180500_006.mkv", DateTime.UtcNow.AddMinutes(-15), 113L * 1024 * 1024, true, false),
        new("ScreenDash_20261012_174500_002.mkv", DateTime.UtcNow.AddDays(-1), 99L * 1024 * 1024, false, false)
        ];
    }
    public async Task<IReadOnlyList<RecordingInfo>> GetRecordingsAsync(CancellationToken cancellationToken = default)
    { if (DelayNextScan) { DelayNextScan = false; await Task.Delay(250, cancellationToken); } return GetRecordings(); }
    public Task SetProtectedAsync(string path, bool protect) => Task.CompletedTask;
    public Task SetProtectedManyAsync(IReadOnlyList<string> paths, bool protect) => Task.CompletedTask;
    public Task SetLabelAsync(string path, string label) => Task.CompletedTask;
    public Task<int> DeleteRecordingsAsync(IReadOnlyList<string> paths, bool allowProtected = false) => Task.FromResult(0);
    public Task ExportAsync(string path, string destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<MediaDetails> GetMediaDetailsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new MediaDetails(TimeSpan.FromMinutes(8.2), 2560, 1440, true));
    public Task ExportClipAsync(string path, string destination, ClipRange range, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void OpenRecordingFolder() { }
    public void OpenLogs() { }
    public void Dispose() { }
}
