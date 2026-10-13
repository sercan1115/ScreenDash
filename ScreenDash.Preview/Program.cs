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
        form.Text = "ScreenDash 1.2 · Vorschau mit Beispieldaten";
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
        Console.WriteLine($"UI checks passed: {checks}");
    }
}

internal sealed class PreviewController : IAppController
{
    public bool Recording { get; set; }
    public bool Paused { get; set; }
    public int RecordingScans { get; private set; }
    public bool DelayNextScan { get; set; }
    public AppSettings Settings { get; } = new() { DisplayId = "test", AudioDeviceId = "speakers" };
    public IReadOnlyList<DisplaySource> Displays { get; } = [new("test", "Bildschirm 1 · 2560 × 1440", 0, 0, 0, 0, 2560, 1440)];
    public IReadOnlyList<AudioSource> AudioDevices { get; } = [new("speakers", "Lautsprecher · Realtek Audio")];
    public DashboardStatus Status => new(Recording, false, Recording ? "Bildschirm 1 wird im Hintergrund aufgenommen. Du kannst ScreenDash minimieren." : "Bereit für die erste Aufnahme", "NVIDIA NVENC", TimeSpan.FromMinutes(17.4), Recording ? 1.7 : 0.2, 83 * 1024L * 1024, 15,
        new(0, 0, 6L * 1024 * 1024 * 1024, 512L * 1024 * 1024, 185L * 1024 * 1024 * 1024, true, ""), Armed: Recording || Paused, Paused: Paused, PauseReason: Paused ? "Manuell pausiert" : "", AudioPeak: Recording ? 0.26 : 0, AudioDevice: "Headset · Spielausgabe");
    public event Action? Changed { add { } remove { } }
    public event Action<string>? Notice { add { } remove { } }
    public Task ToggleRecordingAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public Task PauseAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
    public Task SaveSettingsAsync(AppSettings settings) => Task.CompletedTask;
    public Task RefreshDevicesAsync() => Task.CompletedTask;
    public Task ProtectRecentAsync() => Task.CompletedTask;
    public IReadOnlyList<RecordingInfo> GetRecordings()
    {
        RecordingScans++;
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
    public void OpenRecordingFolder() { }
    public void OpenLogs() { }
    public void Dispose() { }
}
