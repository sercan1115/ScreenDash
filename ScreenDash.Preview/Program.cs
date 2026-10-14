using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using ScreenDash;

internal static class Program
{
    private static bool _clientOnlySnapshots;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "work/ui-preview");
        var dpiArgument = args.FirstOrDefault(arg => arg.StartsWith("--dpi=", StringComparison.Ordinal));
        int dpi = dpiArgument is null ? 96 : int.Parse(dpiArgument[6..]);
        if (dpi is not (96 or 144 or 192)) throw new ArgumentOutOfRangeException(nameof(args), "Preview DPI must be 96, 144 or 192.");
        bool renderOnly = args.Contains("--render-only", StringComparer.Ordinal);
        _clientOnlySnapshots = args.Contains("--client-only", StringComparer.Ordinal);
        Directory.CreateDirectory(output);
        string iconPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ScreenDash", "Assets", "screendash.ico"));
        using var previewIcon = new Icon(iconPath);
        using var controller = new PreviewController();
        using var form = new MainForm(controller, enableHotkeys: false);
        form.Icon = previewIcon;
        form.Text = "ScreenDash 1.5.1 · Vorschau mit Beispieldaten";
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20000, -20000);
        form.ClientSize = new Size(1180, 780);
        form.Show();
        ApplyPreviewDpi(form, dpi);
        SetLogicalClientSize(form, new Size(1180, 780));
        Console.WriteLine($"Preview DPI applied: {form.DeviceDpi}; requested: {dpi}. DPI events are simulated on this off-screen window.");
        if (args.Contains("--checks-only", StringComparer.Ordinal))
        {
            SetLogicalClientSize(form, new Size(1024, 720)); RunUiChecks(form, controller); return;
        }
        var showPage = typeof(MainForm).GetMethod("ShowPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var refreshDashboard = typeof(MainForm).GetMethod("RefreshDashboard", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string[] names = ["01-Aufnahme", "02-Einstellungen", "03-Aufnahmen", "07-Werkzeuge"];
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
            SaveSnapshot(form, Path.Combine(output, names[page] + ".png"));
        }
        var toolTab = typeof(MainForm).GetMethod("ShowToolTab", BindingFlags.NonPublic | BindingFlags.Instance)!;
        showPage.Invoke(form, [3]);
        for (int tab = 1; tab < 3; tab++)
        {
            toolTab.Invoke(form, [tab]); form.PerformLayout(); Application.DoEvents();
            SaveSnapshot(form, Path.Combine(output, $"08-Werkzeuge-{tab}.png"));
        }
        toolTab.Invoke(form, [0]);
        var settingTab = typeof(MainForm).GetMethod("ShowSettingTab", BindingFlags.NonPublic | BindingFlags.Instance)!;
        showPage.Invoke(form, [1]);
        for (int tab = 1; tab < 4; tab++)
        {
            settingTab.Invoke(form, [tab]); form.PerformLayout(); Application.DoEvents();
            SaveSnapshot(form, Path.Combine(output, $"04-Einstellungen-{tab}.png"));
        }
        SetLogicalClientSize(form, new Size(1024, 720));
        for (int page = 0; page < 4; page++)
        {
            controller.Recording = page == 0; refreshDashboard.Invoke(form, null); showPage.Invoke(form, [page]);
            if (page == 1) settingTab.Invoke(form, [2]);
            form.PerformLayout(); Application.DoEvents();
            SaveSnapshot(form, Path.Combine(output, $"05-Kompakt-{page}.png"));
            if (page == 0)
            {
                var dashboard = (Panel)typeof(MainForm).GetField("_dashboard", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                Console.WriteLine($"Compact dashboard: {dashboard.ClientSize}, grid {dashboard.Controls[0].Size}, DPI {form.DeviceDpi}, scroll {dashboard.DisplayRectangle}");
                if (args.Contains("--layout-report", StringComparer.Ordinal))
                    for (Control? control = dashboard; control is not null; control = control.Parent)
                        Console.WriteLine($"Layout {control.GetType().Name}: bounds {control.Bounds}, client {control.ClientRectangle}, display {control.DisplayRectangle}, padding {control.Padding}, scale DPI {control.DeviceDpi}");
            }
        }
        foreach (var size in new[] { (Name: "09-Gross", Size: new Size(1440, 900)), (Name: "10-Minimum", Size: new Size(980, 660)) })
        {
            SetLogicalClientSize(form, size.Size);
            for (int page = 0; page < 4; page++)
            {
                controller.Recording = page == 0; refreshDashboard.Invoke(form, null); showPage.Invoke(form, [page]);
                if (page == 1) settingTab.Invoke(form, [0]);
                if (page == 3) toolTab.Invoke(form, [0]);
                form.PerformLayout(); Application.DoEvents();
                SaveSnapshot(form, Path.Combine(output, $"{size.Name}-{page}.png"));
            }
        }
        SetLogicalClientSize(form, new Size(1024, 720));
        if (!renderOnly) RunUiChecks(form, controller);
        var clipDialogType = typeof(MainForm).GetNestedType("ClipDialog", BindingFlags.NonPublic)!;
        using var clipDialog = (Form)Activator.CreateInstance(clipDialogType, ["ScreenDash_Beispiel.mkv", new MediaDetails(TimeSpan.FromMinutes(8.2), 2560, 1440, true)])!;
        clipDialog.Icon = previewIcon;
        clipDialog.StartPosition = FormStartPosition.Manual; clipDialog.Location = new Point(-20000, -20000); clipDialog.Show(); clipDialog.PerformLayout(); Application.DoEvents();
        ApplyPreviewDpi(clipDialog, dpi); clipDialog.PerformLayout(); Application.DoEvents();
        SaveSnapshot(clipDialog, Path.Combine(output, "06-Clip.png"));
        RenderFeatureDialogs(controller, output, dpi, previewIcon);
    }

    private static void RenderFeatureDialogs(PreviewController controller, string output, int dpi, Icon icon)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var demoRecording = new RecordingInfo(@"C:\Videos\ScreenDash\ScreenDash_20261014_204000_000.mkv", new DateTime(2026, 10, 14, 18, 40, 0, DateTimeKind.Utc), 109L * 1024 * 1024, true, false, "Demo · Match mit Freunden");
        var bookmarks = new RecordingBookmark[] { new("first", TimeSpan.FromSeconds(152), new DateTime(2026, 10, 14, 18, 42, 32, DateTimeKind.Utc), "Demo · Entscheidender Treffer"), new("second", TimeSpan.FromSeconds(317), new DateTime(2026, 10, 14, 18, 45, 17, DateTimeKind.Utc), "Demo · Das nächste Highlight") };
        (string Type, string File, object[] Args)[] previews = [
            ("BookmarkDialog", "11-Lesezeichen.png", [controller, demoRecording, bookmarks]),
            ("MomentClipDialog", "12-Moment-Clip.png", ["Demo · Entscheidender Treffer", TimeSpan.FromSeconds(152), TimeSpan.FromMinutes(8.2)]),
            ("GamePickerDialog", "13-Spiele-Auswahl.png", [new string[] { "DemoGame.exe", "SecondGame.exe", "ThirdGame.exe" }, new string[] { "DemoGame.exe", "SavedGame.exe" }]),
            ("TimeSearchDialog", "14-Zeitsuche.png", [])
        ];
        foreach (var preview in previews)
        {
            var type = typeof(MainForm).GetNestedType(preview.Type, BindingFlags.NonPublic)!;
            using var dialog = (Form)Activator.CreateInstance(type, preview.Args)!;
            dialog.Icon = icon;
            var logicalSize = dialog.ClientSize;
            if (preview.Type == "TimeSearchDialog")
            {
                ((DateTimePicker)type.GetField("_date", flags)!.GetValue(dialog)!).Value = new DateTime(2026, 10, 14);
                ((DateTimePicker)type.GetField("_time", flags)!.GetValue(dialog)!).Value = new DateTime(2026, 10, 14, 20, 42, 32);
            }
            dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-20000, -20000);
            dialog.Show(); ApplyPreviewDpi(dialog, dpi); SetLogicalClientSize(dialog, logicalSize);
            SaveSnapshot(dialog, Path.Combine(output, preview.File));
        }
    }

    private static void SetLogicalClientSize(Form form, Size size)
    {
        form.ClientSize = new Size(size.Width * form.DeviceDpi / 96, size.Height * form.DeviceDpi / 96);
        LayoutTree(form); Application.DoEvents();
    }

    private static void SaveSnapshot(Form form, string file)
    {
        if (form is MainForm) typeof(MainForm).GetMethod("CompletePageTransition", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(form, null);
        LayoutTree(form); Application.DoEvents();
        bool clientOnly = _clientOnlySnapshots || form.DeviceDpi != 96;
        using var bitmap = new Bitmap(clientOnly ? form.ClientSize.Width : form.Width, clientOnly ? form.ClientSize.Height : form.Height);
        if (clientOnly)
        {
            // Draw the native client tree directly without window chrome. Simulated DPI events
            // also require this path because they cannot change the host's physical window limits or bitmap DC.
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(form.BackColor);
            foreach (Control control in form.Controls.Cast<Control>().Reverse()) control.DrawToBitmap(bitmap, control.Bounds);
        }
        else form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(file, ImageFormat.Png);
        Console.WriteLine(file);
    }

    private static void ApplyPreviewDpi(Form form, int dpi)
    {
        if (form.DeviceDpi == dpi) return;
        // Exercise WinForms' DPI-change path without moving a real application onto a user's monitor.
        double ratio = dpi / (double)form.DeviceDpi;
        var suggested = new NativeRect { Left = -20000, Top = -20000, Right = -20000 + (int)Math.Ceiling(form.Width * ratio), Bottom = -20000 + (int)Math.Ceiling(form.Height * ratio) };
        nint rectangle = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(suggested, rectangle, false);
            SendMessage(form.Handle, 0x02E0, (nint)((dpi << 16) | dpi), rectangle);
            form.PerformLayout(); Application.DoEvents();
            if (form.DeviceDpi != dpi) throw new InvalidOperationException($"The preview DPI event was not applied: {form.DeviceDpi} instead of {dpi}.");
        }
        finally { Marshal.FreeHGlobal(rectangle); }
    }

    private static void LayoutTree(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) LayoutTree(child);
    }

    private static IEnumerable<Control> ControlsIn(Control control)
    {
        yield return control;
        foreach (Control child in control.Controls)
            foreach (Control descendant in ControlsIn(child)) yield return descendant;
    }

    private static double TextContrast(Control control)
    {
        Control? surface = control;
        while (surface is not null && surface.BackColor.A != 255) surface = surface.Parent;
        return ContrastRatio(control.ForeColor, surface?.BackColor ?? Color.White);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte component) { double value = component / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private static void RunUiChecks(MainForm form, PreviewController controller)
    {
        int checks = 0;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        // Application.DoEvents does not keep the UI synchronization context installed as Application.Run does.
        // Reflection actions stand in for real UI events, so their awaits must resume on the same UI thread.
        var previousContext = SynchronizationContext.Current;
        using var uiContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(uiContext);
        T Field<T>(string name) where T : class => (T)typeof(MainForm).GetField(name, flags)!.GetValue(form)!;
        string Tip(string name) => Field<ToolTip>("_tips").GetToolTip(Field<Control>(name)) ?? "";
        object? Call(string name, params object[] args)
        {
            SynchronizationContext.SetSynchronizationContext(uiContext);
            return typeof(MainForm).GetMethod(name, flags)!.Invoke(form, args);
        }
        void Check(bool result, string description) { if (!result) throw new InvalidOperationException("UI check failed: " + description); checks++; }
        Check(form.Visible && form.ShowInTaskbar && form.WindowState == FormWindowState.Normal, "normal launch shows the window and its taskbar entry");
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
        Check(Field<Label>("_headerState").Text.Contains("ÄNDERUNGEN") && Field<Button[]>("_nav")[1].AccessibleName!.Contains("ungespeicherte"), "idle drafts are visible in the header and accessible settings navigation");
        Call("ShowPage", 0);
        Check(Field<Label>("_recordingMessage").Text.Contains("gespeicherte Einstellungen") && Field<Button>("_toggle").Enabled && controller.Settings.Fps != 21, "returning to capture explains that start uses the saved settings without silently saving the draft");
        var draftStartTask = (Task)Call("ToggleRecordingActionAsync")!;
        Check(draftStartTask.IsCompletedSuccessfully && controller.StartedSettings!.Fps == controller.Settings.Fps && controller.StartedSettings.Fps != 21 && controller.SettingsSaves == 0 && Field<Button>("_save").Enabled, "starting from an unsaved draft passes the saved configuration to capture and retains the draft");
        typeof(MainForm).GetField("_uiBusy", flags)!.SetValue(form, true); Call("RefreshDashboard");
        Check(Field<Label>("_headerState").Text.Contains("BEARBEITET") && !Field<Label>("_recordingMessage").Text.Contains("Ungespeicherte") && !Field<Button>("_toggle").Enabled, "busy work takes priority over the idle draft notice");
        typeof(MainForm).GetField("_uiBusy", flags)!.SetValue(form, false);
        controller.Recording = true; Call("RefreshDashboard");
        Check(Field<Label>("_headerState").Text.Contains("AUFNAHME") && !Field<Label>("_recordingMessage").Text.Contains("Ungespeicherte") && !Field<Button>("_save").Enabled, "an active recording takes priority over the idle draft notice and locks save");
        controller.Recording = false; controller.Paused = true; Call("RefreshDashboard");
        Check(Field<Label>("_headerState").Text == "PAUSIERT", "a paused session keeps its state visible even while a draft exists");
        controller.Paused = false; Call("ShowPage", 1); Call("RefreshDashboard");
        Field<Button>("_discard").PerformClick();
        Check(!Field<Button>("_save").Enabled && Field<Label>("_headerState").Text == "BEREIT" && !Field<Button[]>("_nav")[1].AccessibleName!.Contains("ungespeicherte") && !Field<Label>("_recordingMessage").Text.Contains("gespeicherte Einstellungen"), "discard clears draft indicators on every page");
        int savedFps = controller.Settings.Fps; Field<NumericUpDown>("_customFps").Value = 21;
        var saveSettingsTask = (Task)Call("SaveSettingsAsync")!; Application.DoEvents();
        Check(saveSettingsTask.IsCompletedSuccessfully && controller.Settings.Fps == 21 && controller.SettingsSaves == 1 && !Field<Button>("_save").Enabled && Field<Label>("_headerState").Text == "BEREIT", "saving commits the synthetic draft and clears its unsaved state");
        controller.Settings.Fps = savedFps;
        Call("LoadSettings");
        bool savedSystemAudio = controller.Settings.AudioEnabled, savedMicrophoneAudio = controller.Settings.MicrophoneEnabled;
        foreach (var mode in new[] { (System: true, Microphone: false, Text: "Systemton an"), (System: true, Microphone: true, Text: "Systemton + Mikrofon"), (System: false, Microphone: true, Text: "Nur Mikrofon"), (System: false, Microphone: false, Text: "Ohne Ton") })
        {
            controller.Settings.AudioEnabled = mode.System; controller.Settings.MicrophoneEnabled = mode.Microphone; Call("RefreshDashboard");
            Check(Field<Label>("_source").Text.Contains(mode.Text), $"dashboard reports saved audio mode: {mode.Text}");
        }
        controller.Settings.AudioEnabled = savedSystemAudio; controller.Settings.MicrophoneEnabled = savedMicrophoneAudio;
        string savedEncoder = controller.Encoder;
        foreach (string encoder in new[] { "NVIDIA NVENC", "AMD AMF", "Intel Quick Sync", "h264_qsv" })
        {
            controller.Encoder = encoder; Call("RefreshDashboard");
            Check(Field<Label>("_encoderDetail").Text.StartsWith("Hardware") && Tip("_encoderDetail").Contains("GPU"), $"hardware encoder detail accurately describes {encoder}");
        }
        controller.Encoder = "Software · libx264"; Call("RefreshDashboard");
        Check(Field<Label>("_encoderDetail").Text.StartsWith("Software") && Tip("_encoderDetail").Contains("Ausweichlösung"), "a software fallback is labelled as CPU encoding rather than hardware");
        controller.Settings.EncoderMode = "Software"; Call("RefreshDashboard");
        Check(Tip("_encoderDetail").Contains("CPU") && !Tip("_encoderDetail").Contains("Ausweichlösung"), "explicit software encoding explains CPU use without implying fallback");
        controller.Settings.EncoderMode = "Auto"; controller.Encoder = ""; Call("RefreshDashboard");
        Check(Field<Label>("_encoderDetail").Text.Contains("Startprüfung"), "an unselected encoder does not claim hardware was already validated");
        controller.Encoder = savedEncoder; Call("LoadSettings"); Call("RefreshDashboard");
        string[] namedSettings = ["_display", "_audio", "_directory", "_profile", "_encoderChoice", "_customFps", "_customBitrate", "_segment", "_retention", "_maxStorage", "_minFree", "_protectMinutes", "_autoStopMinutes", "_microphone", "_systemGain", "_microphoneGain", "_silenceSeconds"];
        Check(namedSettings.Select(Field<Control>).All(control => !string.IsNullOrWhiteSpace(control.AccessibleName)), "direct and nested settings inputs expose nonempty accessible field names");
        string[] settingsWithUnits = ["_customFps", "_customBitrate", "_segment", "_retention", "_maxStorage", "_minFree", "_protectMinutes", "_autoStopMinutes", "_systemGain", "_microphoneGain", "_silenceSeconds"];
        Check(settingsWithUnits.Select(Field<NumericUpDown>).All(control => !string.IsNullOrWhiteSpace(control.AccessibleDescription)) && Field<NumericUpDown>("_retention").AccessibleName != Field<NumericUpDown>("_maxStorage").AccessibleName, "numeric fields retain their units and distinct purposes for assistive technology");
        Check(!string.IsNullOrWhiteSpace(filter.AccessibleName) && !string.IsNullOrWhiteSpace(date.AccessibleName) && search.AccessibleDescription!.Contains("Notizen"), "archive search and filters explain their purpose without relying on placeholder text");
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
        Check(Field<Label>("_archiveProgress").Text.Contains("Prüfexport"), "selection hints do not overwrite the progress of a pending export");
        Call("CancelExport"); deadline = DateTime.UtcNow.AddSeconds(2);
        while (!heldExportTask.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(heldExportTask.IsCompletedSuccessfully && !Field<Button>("_cancelExport").Visible && Field<Button>("_clip").Enabled, "cancelled export clears its busy state and re-enables tools");
        controller.Recording = false;
        grid.Rows[1].Selected = true; Call("UpdateRecordingActions");
        Check(!Field<Button>("_clip").Enabled, "multiple selected recordings disable clip export");
        Check(!Field<Button>("_play").Enabled && !Field<Button>("_export").Enabled && Field<Label>("_archiveProgress").Text.Contains("eine Aufnahme auswählen"), "multiple-selection hints explain how to enable single-file playback and export");
        controller.ArchiveOverride = [new RecordingInfo("active.mkv", DateTime.UtcNow, 1024, false, true)]; Call("RefreshRecordings", true); grid.Rows[0].Selected = true; Call("UpdateRecordingActions");
        Check(!Field<Button>("_clip").Enabled, "an active recording cannot be clipped");
        Check(!Field<Button>("_play").Enabled && !Field<Button>("_export").Enabled && Field<Label>("_archiveProgress").Text.Contains("läuft noch") && Field<Label>("_archiveProgress").Text.Contains("pausieren oder stoppen"), "active-file hints explain why playback and export are unavailable and how to finish the file");
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
        int startsBeforeDelayedCapture = controller.StartRequests;
        var delayedStart = (Task)Call("ToggleRecordingActionAsync")!; Call("RefreshDashboard");
        Check(!delayedStart.IsCompleted && Field<Button>("_toggle").Enabled && Field<Button>("_toggle").Text.Contains("stoppen") && Field<ToolStripMenuItem>("_trayToggle").Enabled, "stop button and tray remain usable while an encoder start is pending");
        Check(!Field<Button>("_pause").Enabled && !Field<Button>("_protect").Enabled, "other capture actions stay disabled during startup");
        var delayedStop = (Task)Call("ToggleRecordingActionAsync")!;
        Check(controller.StopRequests == 1 && !delayedStop.IsCompleted && !delayedStart.IsCompleted, "a stop request reaches the controller before delayed start completes");
        Check(!Field<Button>("_toggle").Enabled && Field<Button>("_toggle").Text.Contains("gestoppt"), "pending stop has a distinct disabled state");
        Call("ToggleRecordingActionAsync");
        Check(controller.StopRequests == 1 && controller.StartRequests == startsBeforeDelayedCapture + 1, "repeated toggle cannot issue another stop or start while stopping");
        controller.CompleteHeldStop(); deadline = DateTime.UtcNow.AddSeconds(5);
        while ((!delayedStart.IsCompleted || !delayedStop.IsCompleted) && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(delayedStart.IsCompletedSuccessfully && delayedStop.IsCompletedSuccessfully && Field<Button>("_toggle").Enabled && Field<Button>("_toggle").Text.Contains("starten"), $"cancelled startup returns to idle without an error dialog (start {delayedStart.Status}; stop {delayedStop.Status}; button {Field<Button>("_toggle").Enabled}: {Field<Button>("_toggle").Text})");
        Call("LoadSettings"); Call("RefreshDashboard");
        Check(!Field<CheckBox>("_microphoneEnabled").Checked && !Field<ComboBox>("_microphone").Enabled, "microphone remains an explicit opt-in by default");
        Check(!Field<Button>("_testMicrophone").Enabled, "microphone test is disabled until microphone is explicitly enabled");
        Check(!Field<CheckBox>("_gameDetection").Checked && !Field<Button>("_saveGames").Enabled, "opening tools never silently enables game detection");
        Field<CheckBox>("_microphoneEnabled").Checked = true; Field<ComboBox>("_microphone").SelectedIndex = 1; Field<NumericUpDown>("_microphoneGain").Value = 85; Field<NumericUpDown>("_systemGain").Value = 135;
        var audioDraft = (AppSettings)Call("ReadSettings")!;
        Check(audioDraft.MicrophoneEnabled && audioDraft.MicrophoneDeviceId == "usb" && audioDraft.MicrophoneGainPercent == 85 && audioDraft.SystemGainPercent == 135, "microphone, source and separate gain controls round trip");
        Call("LoadDevices", true);
        Check(((AudioSource)Field<ComboBox>("_microphone").SelectedItem!).Id == "usb", "device refresh preserves unsaved microphone selection");
        Field<CheckBox>("_warnOnSilence").Checked = false;
        Check(!Field<NumericUpDown>("_silenceSeconds").Enabled && !((AppSettings)Call("ReadSettings")!).WarnOnSilence, "silence notifications can be disabled without disabling capture");
        Call("LoadSettings"); Call("ShowPage", 3); Application.DoEvents();
        Check(Field<Panel>("_toolsPage").Visible && !Field<Panel>("_dashboard").Visible && Field<Label>("_pageTitle").Text == "Werkzeuge", "tools have a dedicated sidebar page");
        Check(controller.UpdateChecks == 0, "opening tools does not contact GitHub");
        Check(Field<ComboBox>("_savedProfiles").Items.Count == 1 && Field<Button>("_applyProfile").Enabled && !Field<Button>("_saveProfile").Enabled, "saved profiles load while new profiles need a name");
        Check(Field<ComboBox>("_savedProfiles").GetItemText(Field<ComboBox>("_savedProfiles").SelectedItem) == "Demo · Shooter mit Headset", "profile dropdown exposes the user-facing name instead of internal model fields");
        Call("ShowToolTab", 1); Check(Field<Panel[]>("_toolTabs")[1].Visible && !Field<Panel[]>("_toolTabs")[0].Visible, "each tool has an uncluttered tab with only its own controls"); Call("ShowToolTab", 0);
        Field<TextBox>("_profileName").Text = "Demo · Eigener Rückblick";
        var saveProfileTask = (Task)Call("SaveProfileAsync")!; Application.DoEvents();
        Check(saveProfileTask.IsCompletedSuccessfully && controller.ProfileSaves == 1 && Field<ComboBox>("_savedProfiles").Items.Count == 2, "saving a named profile refreshes the list");
        Call("ApplySavedProfileAsync");
        Check(controller.AppliedProfile == "default-profile", "applying a saved profile reaches the controller");
        controller.Recording = true; Call("RefreshDashboard");
        Check(!Field<Button>("_applyProfile").Enabled && !Field<Button>("_saveGames").Enabled && !Field<CheckBox>("_gameDetection").Enabled, "profile and automatic-game settings are locked during recording");
        Check(Field<Button>("_bookmark").Enabled && Field<ToolStripMenuItem>("_trayBookmark").Enabled && Field<Button>("_checkUpdate").Enabled, "recording exposes bookmark in the dashboard and tray, and manual update check");
        var markTask = (Task)Call("AddBookmarkAsync")!;
        Check(markTask.IsCompletedSuccessfully && controller.BookmarksAdded == 1 && Field<Label>("_footer").Text.Contains("ganze Datei gesichert"), "bookmark button saves a moment and explains full-file protection");
        controller.Recording = false; controller.Paused = true; Call("RefreshDashboard");
        Check(!Field<Button>("_bookmark").Enabled, "paused capture cannot mark a misleading new media position");
        controller.Paused = false; Call("RefreshDashboard");
        Field<TextBox>("_gameNames").Text = "game.exe, second-game.exe"; Field<CheckBox>("_gameDetection").Checked = true;
        Check(Field<Button>("_saveGames").Enabled, "game selection remains a draft until saved");
        var saveGameTask = (Task)Call("SaveGamesAsync")!;
        Check(saveGameTask.IsCompletedSuccessfully && !Field<Button>("_saveGames").Enabled, "saving game selection clears the dirty state");
        Call("ShowToolTab", 2); var checkUpdateTask = (Task)Call("CheckUpdateAsync")!; Application.DoEvents();
        Check(checkUpdateTask.IsCompletedSuccessfully && controller.UpdateChecks == 1 && Field<Button>("_downloadUpdate").Enabled && !Field<Button>("_launchUpdate").Enabled, "manual update check offers preparation without automatically launching");
        controller.DelayNextUpdate = true; var prepareTask = (Task)Call("PrepareUpdateAsync")!;
        Check(!prepareTask.IsCompleted && Field<Button>("_cancelUpdate").Visible && !Field<Button>("_downloadUpdate").Enabled, "update preparation is asynchronous and cancellable");
        Field<Button>("_cancelUpdate").PerformClick(); deadline = DateTime.UtcNow.AddSeconds(2);
        while (!prepareTask.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        Check(prepareTask.IsCompletedSuccessfully && !Field<Button>("_launchUpdate").Enabled && Field<Button>("_downloadUpdate").Enabled, "cancelled preparation does not expose an unverified update");
        prepareTask = (Task)Call("PrepareUpdateAsync")!; Application.DoEvents();
        Check(prepareTask.IsCompletedSuccessfully && Field<Button>("_launchUpdate").Enabled && !Field<Button>("_downloadUpdate").Enabled, "prepared update requires a distinct restart action");
        Check(Field<Button>("_openUpdateFolder").Enabled && Field<TextBox>("_updateDirectory").ReadOnly && Field<TextBox>("_updateDirectory").Text.Contains("DemoUpdate"), "prepared portable update exposes its new versions folder");
        var updateHelp = Field<Panel[]>("_toolTabs")[2].Controls.Cast<Control>().SelectMany(ControlsIn).OfType<Label>().Select(label => label.Text).FirstOrDefault(text => text.Contains("Desktopverknüpfung"));
        Check(updateHelp is not null && updateHelp.Contains("automatisch") && updateHelp.Contains("manuell erstellte"), "portable update help explains automatic switching and the separate handling of manual shortcuts");
        var momentType = typeof(MainForm).GetNestedType("MomentClipDialog", BindingFlags.NonPublic)!;
        using var moment = (Form)Activator.CreateInstance(momentType, ["Demo · Moment", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(40)])!;
        moment.StartPosition = FormStartPosition.Manual; moment.Location = new Point(-20000, -20000); moment.Show(); Application.DoEvents();
        ClipRange MomentRange() => (ClipRange)momentType.GetProperty("SelectedRange")!.GetValue(moment)!;
        Check(MomentRange().Start == TimeSpan.Zero && MomentRange().Duration == TimeSpan.FromSeconds(25), "bookmark clip clamps its pre-roll to the beginning");
        ((NumericUpDown)momentType.GetField("_after", flags)!.GetValue(moment)!).Value = 300;
        Check(MomentRange().Duration == TimeSpan.FromSeconds(40), "bookmark clip clamps its post-roll to the recording end");
        ((NumericUpDown)momentType.GetField("_before", flags)!.GetValue(moment)!).Value = 0; ((NumericUpDown)momentType.GetField("_after", flags)!.GetValue(moment)!).Value = 0;
        Check(!((Button)momentType.GetField("_accept", flags)!.GetValue(moment)!).Enabled, "zero-length bookmark clip cannot be accepted");
        var gamePickerType = typeof(MainForm).GetNestedType("GamePickerDialog", BindingFlags.NonPublic)!;
        using var gamePicker = (Form)Activator.CreateInstance(gamePickerType, [new string[] { "Game.exe", "Game.exe", "New.exe" }, new string[] { "Saved.exe", "Game.exe" }])!;
        var pickedNames = (IReadOnlyList<string>)gamePickerType.GetProperty("SelectedNames")!.GetValue(gamePicker)!;
        Check(pickedNames.Count == 2 && pickedNames.Contains("Saved.exe") && pickedNames.Contains("Game.exe"), "game picker preserves saved selections and removes duplicate process names");
        var timeType = typeof(MainForm).GetNestedType("TimeSearchDialog", BindingFlags.NonPublic)!;
        using var timeDialog = (Form)Activator.CreateInstance(timeType)!;
        ((DateTimePicker)timeType.GetField("_date", flags)!.GetValue(timeDialog)!).Value = new DateTime(2026, 10, 14);
        ((DateTimePicker)timeType.GetField("_time", flags)!.GetValue(timeDialog)!).Value = new DateTime(2026, 10, 14, 20, 41, 12);
        var localMoment = (DateTime)timeType.GetProperty("SelectedTime")!.GetValue(timeDialog)!;
        Check(localMoment.Kind == DateTimeKind.Local && localMoment == new DateTime(2026, 10, 14, 20, 41, 12), "time search combines date and clock time in local Windows time");
        Call("ShowPage", 2); controller.ArchiveOverride = Enumerable.Range(0, 750).Select(i => new RecordingInfo($"record-{i:0000}.mkv", DateTime.UtcNow.AddMinutes(-i), i * 1024L, false, false)).ToArray(); Call("RefreshRecordings", true); var target = controller.ArchiveOverride[605]; Call("SelectArchiveRecording", target);
        Check(Field<Label>("_archivePageLabel").Text.StartsWith("Seite 4") && grid.SelectedRows.Count == 1 && ((RecordingInfo)grid.SelectedRows[0].Tag!).Path == target.Path, "time search can reveal and select a match beyond the first archive page");
        Check(Field<Button>("_bookmarksSelected").Enabled, "single archive selection exposes saved moments");
        // Native control animations must stop promptly and never turn idle screens into a render loop.
        Call("SetMotionEnabled", true); Call("CompletePageTransition");
        var pageTimer = Field<System.Windows.Forms.Timer>("_pageMotionTimer");
        int MotionFrames() => (int)typeof(MainForm).GetField("_motionFrameCount", flags)!.GetValue(form)!;
        void PumpFor(int milliseconds)
        {
            var until = System.Diagnostics.Stopwatch.StartNew();
            while (until.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); }
        }
        Check(!pageTimer.Enabled && Field<Panel>("_content").Padding == Padding.Empty, "settled UI has no page animation timer or residual offset");
        int frames = MotionFrames(); Call("BeginPageTransition");
        bool systemMotion = SystemInformation.IsMenuAnimationEnabled && !SystemInformation.HighContrast;
        Check(pageTimer.Enabled == systemMotion, "visible page transitions respect the Windows animation and high contrast preferences");
        PumpFor(350);
        Check(!pageTimer.Enabled && (systemMotion ? MotionFrames() > frames : MotionFrames() == frames) && Field<Panel>("_content").Padding == Padding.Empty, "page transition completes and removes its temporary offset");
        frames = MotionFrames(); PumpFor(80);
        Check(MotionFrames() == frames, "settled pages do not schedule further animation frames");
        Call("BeginPageTransition"); form.Hide(); Application.DoEvents();
        Check(!pageTimer.Enabled && Field<Panel>("_content").Padding == Padding.Empty, "hiding the window immediately stops animation and resets layout");
        frames = MotionFrames(); Call("BeginPageTransition"); PumpFor(40);
        Check(!pageTimer.Enabled && MotionFrames() == frames, "hidden windows never begin a transition");
        form.Show(); Call("SetMotionEnabled", false); Call("BeginPageTransition");
        Check(!pageTimer.Enabled && Field<Panel>("_content").Padding == Padding.Empty, "disabled motion keeps navigation immediate and timer-free");
        Call("SetMotionEnabled", true);
        for (int i = 0; i < 40; i++) Call("ShowPage", i % 4);
        Call("CompletePageTransition"); Application.DoEvents();
        Check(Field<Panel>("_toolsPage").Visible && !Field<Panel>("_dashboard").Visible && !Field<Panel>("_settingsPage").Visible && !Field<Panel>("_recordingsPage").Visible, "rapid page navigation leaves exactly the final page visible");
        Check(!pageTimer.Enabled && Field<Panel>("_content").Padding == Padding.Empty, "rapid navigation does not retain queued animation or a shifted page");
        Call("ShowPage", 2); Call("CompletePageTransition"); form.PerformLayout(); Application.DoEvents();
        Check(grid.ClientSize.Width > 500 * form.DeviceDpi / 96 && grid.ClientSize.Height > 150 * form.DeviceDpi / 96, "compact archive preserves a readable table viewport");
        Check(grid.Columns.Cast<DataGridViewColumn>().Where(column => column.Visible).All(column => column.Width >= column.MinimumWidth), "archive columns respect their readable minimum widths");
        Check(Field<TextBox>("_search").Width >= 140 * form.DeviceDpi / 96 && Field<ComboBox>("_dateFilter").Height > 0, "compact archive retains a usable search and date filter");
        Call("ShowPage", 0); Call("CompletePageTransition"); form.PerformLayout(); Application.DoEvents();
        Check(!Field<Panel>("_dashboard").VerticalScroll.Visible, "compact dashboard presents its controls and metrics without a page scrollbar");
        Check(Field<Label>("_encoderDetail").Visible && Field<Label>("_encoderDetail").Parent is not null, "the live encoder detail is connected to the visible dashboard metric");
        Check(new[] { "_toggle", "_pause", "_protect", "_bookmark" }.Select(Field<Button>).All(button => button.Parent!.ClientRectangle.Contains(button.Bounds)), "all four dashboard capture actions stay inside their compact viewport");
        Check(new[] { "_toggle", "_pause", "_protect", "_bookmark" }.Select(Field<Button>).All(button => TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 44 * form.DeviceDpi / 96 <= button.ClientSize.Width), "compact capture buttons fit their full action labels alongside their icons");
        string[] bodyText = ["_pageSubtitle", "_recordingMessage", "_source", "_footer", "_encoderDetail", "_autoStopCountdown"];
        Check(bodyText.Select(Field<Label>).All(label => TextContrast(label) >= 4.5), "dashboard body text meets readable contrast against its actual surfaces");
        Check(TextContrast(Field<Label>("_pageTitle")) >= 3 && TextContrast(Field<Label>("_elapsed")) >= 3 && ContrastRatio(Field<Button>("_toggle").ForeColor, Field<Button>("_toggle").BackColor) >= 4.5, "large display text and the primary capture action remain legible");
        var hoverButton = Field<Button[]>("_nav")[0];
        var hoverType = hoverButton.GetType();
        bool HoverRunning() => (bool)hoverType.GetProperty("AnimationRunning")!.GetValue(hoverButton)!;
        void HoverEvent(string name) => hoverType.GetMethod(name, flags)!.Invoke(hoverButton, [EventArgs.Empty]);
        Check(!HoverRunning(), "idle navigation buttons do not run an animation timer");
        HoverEvent("OnMouseEnter");
        Check(HoverRunning() == systemMotion, "button hover follows Windows motion preferences");
        PumpFor(260); Check(!HoverRunning(), "button hover stops once its finite highlight transition completes");
        HoverEvent("OnMouseLeave"); form.Hide(); Application.DoEvents();
        Check(!HoverRunning(), "hiding the window also stops button hover timers");
        form.Show(); Call("SetMotionEnabled", false); HoverEvent("OnMouseEnter");
        Check(!HoverRunning(), "motion disabled also prevents animated button hovers");
        HoverEvent("OnMouseLeave"); Call("SetMotionEnabled", true); Call("CompletePageTransition");
        Point restoredLocation = form.Location;
        Size restoredClientSize = form.ClientSize;
        int stopRequests = controller.StopRequests;
        controller.Recording = true; Call("RefreshDashboard");
        HoverEvent("OnMouseEnter"); Call("BeginPageTransition");
        Check(pageTimer.Enabled == systemMotion && HoverRunning() == systemMotion, "visible recording window can start finite page and hover transitions");
        form.WindowState = FormWindowState.Minimized; Application.DoEvents();
        Check(form.Visible && form.ShowInTaskbar && form.WindowState == FormWindowState.Minimized, "minimizing retains the visible native window and taskbar entry");
        Check(!pageTimer.Enabled && !HoverRunning() && Field<Panel>("_content").Padding == Padding.Empty, "minimizing immediately finishes page and hover motion without requiring the window to be hidden");
        frames = MotionFrames(); Call("BeginPageTransition"); HoverEvent("OnMouseLeave"); HoverEvent("OnMouseEnter"); PumpFor(100);
        Check(!pageTimer.Enabled && !HoverRunning() && MotionFrames() == frames, "minimized taskbar windows never start motion or schedule animation frames");
        Check(controller.Recording && controller.StopRequests == stopRequests, "minimizing leaves an active recording running");
        Call("ShowWindow"); Application.DoEvents();
        Check(form.Visible && form.ShowInTaskbar && form.WindowState == FormWindowState.Normal, "opening the minimized application restores its normal visible taskbar window");
        Check(form.Location == restoredLocation && form.ClientSize == restoredClientSize && Field<Panel>("_content").Padding == Padding.Empty && !pageTimer.Enabled && !HoverRunning(), "restoring preserves the previous bounds and leaves no temporary animation offset or timer");
        controller.Settings.ShowNotifications = false;
        form.Close(); Application.DoEvents();
        Check(!form.IsDisposed && form.Visible && form.ShowInTaskbar && form.WindowState == FormWindowState.Minimized, "closing an active recording minimizes to the taskbar instead of hiding or disposing the window");
        Check(controller.Recording && controller.StopRequests == stopRequests, "closing an active recording preserves capture and never requests an implicit stop");
        Call("ShowWindow"); controller.Recording = false; Call("RefreshDashboard");
        Check(form.Visible && form.WindowState == FormWindowState.Normal && Field<Panel>("_content").Padding == Padding.Empty, "the taskbar window can be reopened after an active-recording close");
        controller.Settings.MinimizeOnRecord = true;
        controller.Recording = true; Call("RefreshDashboard"); Application.DoEvents();
        Check(form.Visible && form.ShowInTaskbar && form.WindowState == FormWindowState.Minimized && controller.StopRequests == stopRequests, "the optional minimize-on-record action keeps the running capture in the taskbar");
        Call("ShowWindow"); Call("RefreshDashboard"); Application.DoEvents();
        Check(form.Visible && form.WindowState == FormWindowState.Normal, "opening an ongoing recording does not immediately minimize it again");
        controller.Recording = false; controller.Settings.MinimizeOnRecord = false; Call("RefreshDashboard");
        using (var startupController = new PreviewController())
        using (var startupForm = new MainForm(startupController, startMinimized: true, enableHotkeys: false))
        {
            startupController.Settings.ShowNotifications = false;
            startupForm.StartPosition = FormStartPosition.Manual; startupForm.Location = new Point(-20000, -20000);
            startupForm.Show(); Application.DoEvents();
            Check(startupForm.Visible && startupForm.ShowInTaskbar && startupForm.WindowState == FormWindowState.Minimized, "explicit minimized startup creates a taskbar window instead of a hidden tray-only window");
            typeof(MainForm).GetMethod("ShowWindow", flags)!.Invoke(startupForm, null); Application.DoEvents();
            Check(startupForm.Visible && startupForm.ShowInTaskbar && startupForm.WindowState == FormWindowState.Normal && startupController.StartRequests == 0 && startupController.StopRequests == 0, "restoring an explicitly minimized startup leaves capture idle and creates no start or stop requests");
        }
        HoverEvent("OnMouseLeave");
        Call("SetMotionEnabled", false);
        Console.WriteLine($"UI checks passed: {checks}");
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }
}

internal sealed class PreviewController : IAppController
{
    public int SettingsSaves { get; private set; }
    public string Encoder { get; set; } = "NVIDIA NVENC";
    public AppSettings? StartedSettings { get; private set; }
    public int BookmarksAdded { get; private set; }
    public int ProfileSaves { get; private set; }
    public int UpdateChecks { get; private set; }
    public bool DelayNextUpdate { get; set; }
    public string? AppliedProfile { get; private set; }
    private readonly List<RecordingProfile> _profiles = [new("default-profile", "Demo · Shooter mit Headset", new AppSettings { DisplayId = "test", AudioDeviceId = "headset" })];
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
    public AppSettings Settings { get; private set; } = new() { RecordingDirectory = @"C:\Videos\ScreenDash", DisplayId = "test", AudioDeviceId = "speakers", AutoStopMinutes = 60 };
    public IReadOnlyList<DisplaySource> Displays { get; } = [new("test", "Bildschirm 1 · 2560 × 1440", 0, 0, 0, 0, 2560, 1440), new("second", "Bildschirm 2 · 1920 × 1080", 0, 1, 2560, 0, 1920, 1080)];
    public IReadOnlyList<AudioSource> Microphones { get; } = [new("mic", "Headset · Mikrofon"), new("usb", "USB · Mikrofon")];
    public IReadOnlyList<AudioSource> AudioDevices { get; } = [new("speakers", "Lautsprecher · Realtek Audio"), new("headset", "Headset · Spielausgabe")];
    public DashboardStatus Status => new(Recording, StartPending || StopPending, Recording ? "Bildschirm 1 wird im Hintergrund aufgenommen. Du kannst ScreenDash minimieren." : "Bereit für die erste Aufnahme", Encoder, TimeSpan.FromMinutes(17.4), Recording ? 1.7 : 0.2, 83 * 1024L * 1024, 15,
        new(0, 0, 6L * 1024 * 1024 * 1024, 512L * 1024 * 1024, 185L * 1024 * 1024 * 1024, true, ""), Armed: Recording || Paused || StartPending && !_startCanceled, Paused: Paused, PauseReason: Paused ? "Manuell pausiert" : "", AudioPeak: Recording ? 0.26 : 0, AudioDevice: "Headset · Spielausgabe", Remaining: Recording || Paused ? Remaining : null);
    public event Action? Changed;
    public event Action<string>? Notice { add { } remove { } }
    public async Task ToggleRecordingAsync()
    {
        StartRequests++; StartedSettings = Settings.Clone();
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
    public Task SaveSettingsAsync(AppSettings settings) { SettingsSaves++; Settings = settings.Clone(); return Task.CompletedTask; }
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
    public Task AddBookmarkAsync(string label = "") { BookmarksAdded++; return Task.CompletedTask; }
    public Task<IReadOnlyList<RecordingBookmark>> GetBookmarksAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RecordingBookmark>>([new("first", TimeSpan.FromMinutes(2), DateTime.UtcNow, "Demo · Treffer"), new("second", TimeSpan.FromMinutes(5), DateTime.UtcNow, "Demo · Zweiter Moment")]);
    public Task RenameBookmarkAsync(string path, string id, string label) => Task.CompletedTask;
    public Task DeleteBookmarkAsync(string path, string id) => Task.CompletedTask;
    public Task<RecordingTimeMatch?> FindByTimeAsync(DateTime localTime, CancellationToken cancellationToken = default) => Task.FromResult<RecordingTimeMatch?>(new(GetRecordings().First(r => !r.Active), TimeSpan.FromSeconds(32)));
    public Task<IReadOnlyList<RecordingProfile>> GetProfilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RecordingProfile>>(_profiles.ToArray());
    public Task SaveProfileAsync(string name, AppSettings settings) { ProfileSaves++; _profiles.Add(new("profile-" + ProfileSaves, name, settings.Clone())); return Task.CompletedTask; }
    public Task ApplyProfileAsync(string id) { AppliedProfile = id; return Task.CompletedTask; }
    public Task DeleteProfileAsync(string id) { _profiles.RemoveAll(p => p.Id == id); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> GetRunningGamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["DemoGame.exe", "SecondGame.exe"]);
    public Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default) { UpdateChecks++; return Task.FromResult(new UpdateInfo("1.5.0", true, "https://github.com/sercan1115/ScreenDash/releases/tag/v1.5.0", "https://github.com/sercan1115/ScreenDash/releases/download/v1.5.0/ScreenDash-1.5-Windows.zip", "https://github.com/sercan1115/ScreenDash/releases/download/v1.5.0/SHA256SUMS.txt", 70000000)); }
    public async Task<PreparedUpdate> PrepareUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken cancellationToken = default) { if (DelayNextUpdate) { DelayNextUpdate = false; await Task.Delay(200, cancellationToken); } progress?.Report(1); return new(info.Version, "DemoUpdate", "DemoUpdate/ScreenDash.exe"); }
    public void LaunchPreparedUpdate(PreparedUpdate update) => throw new InvalidOperationException("Preview never launches an update");
    public void OpenRecordingFolder() { }
    public void OpenLogs() { }
    public void Dispose() { }
}
