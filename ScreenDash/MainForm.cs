using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenDash;

public sealed partial class MainForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(14, 15, 17), Sidebar = Color.FromArgb(8, 9, 11), Card = Color.FromArgb(23, 25, 28), Input = Color.FromArgb(33, 36, 40);
    private static readonly Color Ink = Color.FromArgb(239, 241, 244), Muted = Color.FromArgb(170, 175, 183), Cyan = Color.FromArgb(211, 216, 222), Violet = Color.FromArgb(145, 153, 164), Line = Color.FromArgb(60, 65, 73), Amber = Color.FromArgb(239, 241, 244);
    private readonly IAppController _controller;
    private readonly bool _startMinimized;
    private readonly bool _enableHotkeys;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _trayToggle, _trayPause, _trayProtect;
    private readonly ToolTip _tips = new() { InitialDelay = 500, AutoPopDelay = 9000, ReshowDelay = 100 };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Margin = Padding.Empty }, _dashboard = new() { Dock = DockStyle.Fill }, _settingsPage = new() { Dock = DockStyle.Fill }, _recordingsPage = new() { Dock = DockStyle.Fill };
    private readonly Button[] _nav = new Button[4], _settingNav = new Button[4];
    private readonly Panel[] _settingTabs = new Panel[4];
    private readonly Label _pageTitle = TextLabel("Aufnahme", 27, Ink, FontStyle.Bold), _pageSubtitle = TextLabel("Dein Bildschirm. Als sicherer Rückblick.", 10, Muted), _headerState = new StatusPill { Text = "BEREIT", Dock = DockStyle.Fill, ForeColor = Cyan, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = new Padding(0, 6, 0, 6) };
    private readonly Label _recordingState = TextLabel("Bereit für deinen Rückblick", 22, Ink, FontStyle.Bold), _recordingMessage = TextLabel("", 10, Muted), _elapsed = TextLabel("00:00:00", 28, Ink, FontStyle.Bold), _source = TextLabel("", 10, Muted);
    private readonly Label _encoder = TextLabel("GPU bevorzugt", 16, Ink, FontStyle.Bold), _cpu = TextLabel("0,0 %", 24, Ink, FontStyle.Bold), _memory = TextLabel("0 MB", 24, Ink, FontStyle.Bold), _fps = TextLabel("15 FPS", 24, Ink, FontStyle.Bold);
    private readonly Label _encoderDetail = TextLabel("H.264 · Startprüfung", 8, Muted);
    private readonly Label _storage = TextLabel("Speicher wird geprüft …", 14, Ink, FontStyle.Bold), _storageDetail = TextLabel("", 9, Muted), _storageEstimate = TextLabel("", 10, Cyan);
    private readonly Label _footer = TextLabel("Alles bleibt auf deinem PC.  ·  Keine Cloud, kein Upload.", 9, Muted), _settingsHint = TextLabel("", 9, Muted), _qualityEstimate = TextLabel("", 10, Cyan), _quotaEstimate = TextLabel("", 10, Cyan);
    private readonly StorageMeter _storageMeter = new() { Dock = DockStyle.Fill };
    private readonly Button _toggle = ActionButton("●  Aufnahme starten", true), _pause = ActionButton("Ⅱ  Pause"), _protect = ActionButton("◇  Letzte 10 Min. sichern"), _save = ActionButton("Einstellungen speichern", true), _discard = ActionButton("Verwerfen"), _refreshDevices = ActionButton("Geräte aktualisieren");
    private readonly Button _play = ActionButton("Abspielen"), _protectSelected = ActionButton("Sichern"), _releaseSelected = ActionButton("Freigeben"), _export = ActionButton("MP4 exportieren"), _clip = ActionButton("Clip exportieren"), _labelSelected = ActionButton("Benennen"), _delete = ActionButton("Löschen"), _cancelExport = ActionButton("Abbrechen"), _clearFilters = ActionButton("Zurücksetzen"), _previousArchivePage = ActionButton("← Zurück"), _nextArchivePage = ActionButton("Weiter →");
    private readonly Label _archivePageLabel = TextLabel("", 9, Muted), _autoStopCountdown = TextLabel("Auto-Stopp aus", 9, Muted);
    private const int ArchivePageSize = 200;
    private int _archivePage, _archivePageCount = 1;
    private readonly ContextMenuStrip _archiveMenu = DarkMenu();
    private readonly TableLayoutPanel _archiveProgressLayout = Grid(3, 1);
    private readonly CancellationTokenSource _archiveReloadCancellation = new();
    private int _dashboardUpdateQueued;
    private readonly CheckBox _singleFile = Check("Eine durchgehende Datei pro Aufnahme (empfohlen)");
    private readonly CheckBox _autoStopEnabled = Check("Aufnahme automatisch nach einer festen Dauer stoppen");
    private readonly Label _sound = TextLabel("Tonprüfung: Aufnahme starten", 9, Muted);
    private readonly Label _audioTestResult = TextLabel("Starte das Spiel und prüfe, ob Ton ankommt.", 9, Muted);
    private CancellationTokenSource? _exportCancellation;
    private Task? _activeExportTask;
    private readonly DataGridView _recordings = new();
    private readonly Label _recordingsSummary = TextLabel("", 10, Muted), _archiveProgress = TextLabel("Mehrere Abschnitte mit Strg oder Umschalt auswählen.", 9, Muted), _emptyArchive = TextLabel("Noch kein Rückblick vorhanden\n\nStarte eine Aufnahme. Deine Abschnitte erscheinen automatisch hier.", 12, Muted);
    private readonly ProgressBar _exportProgress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 35, Visible = false };
    private readonly TextBox _search = Entry(), _directory = Entry();
    private readonly ComboBox _archiveFilter = Choice(), _dateFilter = Choice(), _display = Choice(), _audio = Choice(), _profile = Choice(), _encoderChoice = Choice();
    private readonly CheckBox _audioEnabled = Check("Systemton aufnehmen"), _cursor = Check("Mauszeiger im Video anzeigen"), _software = Check("CPU-Kodierung als Ausweichlösung erlauben"), _pauseOnLock = Check("Bei gesperrtem Windows pausieren"), _recordOnLaunch = Check("Beim Programmstart direkt aufnehmen"), _startWithWindows = Check("ScreenDash mit Windows starten"), _notifications = Check("Hinweise im Windows-Infobereich anzeigen"), _minimizeOnRecord = Check("Nach dem Aufnahmestart in die Taskleiste minimieren");
    private readonly NumericUpDown _segment = Number(1, 60), _retention = Number(1, 365), _maxStorage = Number(1, 4096), _minFree = Number(1, 128), _protectMinutes = Number(1, 180), _customFps = Number(5, 60), _customBitrate = Number(500, 50000), _autoStopMinutes = Number(1, 1440);
    private readonly Panel _settingsFields = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private bool _uiBusy, _stopping, _exitRequested, _exitInProgress, _allowClose, _hiddenNoticeShown, _hotkeyToggle, _hotkeyProtect, _hotkeyPause, _loadingSettings, _updatingProfile, _exporting, _refreshingArchive, _wasRecording, _settingsDirty, _archiveLoading, _archiveReloadPending;
    private string _settingsBaseline = "", _archiveLoadError = "", _archiveSortColumn = "Time";
    private bool _archiveSortDescending = true;
    private long _lastRecordingsRefresh;
    private int _page;
    private IReadOnlyList<RecordingInfo> _archiveCache = Array.Empty<RecordingInfo>();

    public MainForm(IAppController controller, bool startMinimized = false, bool enableHotkeys = true)
    {
        _controller = controller; _startMinimized = startMinimized; _enableHotkeys = enableHotkeys;
        Text = "ScreenDash 1.5.1 · Bildschirm-Dashcam"; StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true;
        ClientSize = new Size(1180, 780); MinimumSize = new Size(980, 660); AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 10); DoubleBuffered = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; } catch { Icon = SystemIcons.Application; }
        _trayMenu = DarkMenu(); _trayMenu.Items.Add("ScreenDash öffnen", null, (_, _) => ShowWindow());
        _trayToggle = new ToolStripMenuItem("Aufnahme starten", null, async (_, _) => await ToggleRecordingActionAsync());
        _trayPause = new ToolStripMenuItem("Pause", null, async (_, _) => await RunActionAsync(TogglePauseAsync));
        _trayProtect = new ToolStripMenuItem("Letzte Minuten sichern", null, async (_, _) => await RunActionAsync(_controller.ProtectRecentAsync));
        _trayBookmark = new ToolStripMenuItem("Moment markieren", null, async (_, _) => await AddBookmarkAsync()) { ShortcutKeyDisplayString = "Strg + Alt + F11" };
        _trayMenu.Items.AddRange([_trayToggle, _trayPause, _trayProtect, _trayBookmark, new ToolStripSeparator()]);
        _trayMenu.Items.Add("Aufnahmeordner öffnen", null, (_, _) => RunSync(_controller.OpenRecordingFolder));
        _trayMenu.Items.Add("Desktop-Verknüpfung erstellen", null, (_, _) => RunSync(() =>
        {
            WindowsIntegration.EnsureDesktopShortcut(Application.ExecutablePath);
            _footer.Text = "Desktop-Verknüpfung ist bereit.";
            Notify("Desktop-Verknüpfung ist bereit.");
        }));
        _trayMenu.Items.Add("Beenden", null, async (_, _) => await ExitAsync());
        _tray = new NotifyIcon { Text = "ScreenDash · bereit", Icon = Icon, Visible = true, ContextMenuStrip = _trayMenu }; _tray.DoubleClick += (_, _) => ShowWindow();
        BuildShell(); BuildDashboard(); BuildSettings(); BuildRecordings(); BuildTools(); TrackSettingsChanges();
        _content.Controls.AddRange([_dashboard, _settingsPage, _recordingsPage, _toolsPage]); ShowPage(0); LoadSettings(); RefreshDashboard();
        InitializeMotion();
        _controller.Changed += ControllerChanged; _controller.Notice += ControllerNotice; FormClosing += HandleFormClosing;
        Shown += (_, _) => { FitWorkingArea(); if (_startMinimized) BeginInvoke((Action)MinimizeToTaskbar); };
    }

    private void BuildSettings()
    {
        var page = Grid(1, 4); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); page.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        var tabBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty }; string[] titles = ["Quelle", "Qualität", "Speicher", "Automatik"];
        for (int i = 0; i < titles.Length; i++) { int tab = i; _settingNav[i] = ActionButton(titles[i]); _settingNav[i].Size = new Size(125, 36); _settingNav[i].Click += (_, _) => ShowSettingTab(tab); tabBar.Controls.Add(_settingNav[i]); _settingTabs[i] = Surface(new Padding(22, 17, 22, 16)); _settingTabs[i].Margin = Padding.Empty; _settingTabs[i].AutoScroll = true; _settingsFields.Controls.Add(_settingTabs[i]); }
        page.Controls.Add(tabBar, 0, 0); page.Controls.Add(_settingsHint, 0, 1); page.Controls.Add(_settingsFields, 0, 2);
        var sourceFields = SettingsGrid(_settingTabs[0]); AddHeading(sourceFields, "Was möchtest du aufnehmen?", "Bildschirm, Spielton und auf Wunsch deine Stimme."); AddField(sourceFields, "Bildschirm", _display); AddField(sourceFields, "Audioausgabe", _audio); AddOption(sourceFields, _audioEnabled); AddOption(sourceFields, _cursor); _audioEnabled.CheckedChanged += (_, _) => { _audio.Enabled = _systemGain.Enabled = _audioEnabled.Checked; }; BuildAudioFeatures(sourceFields);
        var sourceActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty }; _refreshDevices.Size = new Size(192, 35); _refreshDevices.Click += async (_, _) => await RunActionAsync(async () => { await _controller.RefreshDevicesAsync(); LoadDevices(); }); sourceActions.Controls.Add(_refreshDevices); var testAudio = ActionButton("Ton testen"); testAudio.Size = new Size(118, 35); testAudio.Click += async (_, _) => await TestAudioAsync(); sourceActions.Controls.Add(testAudio); var mixer = ActionButton("Mixer öffnen"); mixer.Size = new Size(128, 35); mixer.Click += (_, _) => RunSync(() => Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true })); sourceActions.Controls.Add(mixer); AddField(sourceFields, "", sourceActions, 47); AddWide(sourceFields, _audioTestResult, 48); AddHelp(sourceFields, "Nur die gewählte Audioausgabe wird aufgenommen.\nEin Gerätewechsel in Windows wird nach „Geräte aktualisieren“ sichtbar.");
        var qualityFields = SettingsGrid(_settingTabs[1]); AddHeading(qualityFields, "Wenig Last. Genug Detail.", "Die GPU übernimmt die Kodierung, sofern ein passender Encoder verfügbar ist.");
        _profile.Items.AddRange(["Ultra sparsam · 10 FPS · 1,5 Mbit/s", "Sparsam · 15 FPS · 3 Mbit/s (empfohlen)", "Standard · 30 FPS · 6 Mbit/s", "Detail · 30 FPS · 10 Mbit/s", "Benutzerdefiniert"]); _encoderChoice.Items.AddRange(["Auto · GPU bevorzugt", "NVIDIA · NVENC", "AMD · AMF", "Intel · Quick Sync", "Software · CPU"]);
        AddField(qualityFields, "Leistungsprofil", _profile); AddField(qualityFields, "Bildrate", WithUnit(_customFps, "FPS · weniger Bilder entlasten GPU und Speicher")); AddField(qualityFields, "Video-Bitrate", WithUnit(_customBitrate, "kbit/s · höher bedeutet mehr Details und größere Dateien")); AddField(qualityFields, "Encoder", _encoderChoice); AddOption(qualityFields, _software); AddWide(qualityFields, _qualityEstimate, 48);
        _profile.SelectedIndexChanged += (_, _) => ApplyProfile(); _customFps.ValueChanged += (_, _) => CustomQualityChanged(); _customBitrate.ValueChanged += (_, _) => CustomQualityChanged(); _audioEnabled.CheckedChanged += (_, _) => UpdateEstimates();
        var storageFields = SettingsGrid(_settingTabs[2]); AddHeading(storageFields, "Dein Rückblick hat ein Limit.", "Zeitlimit und Speicherlimit gelten gemeinsam. Gesicherte Abschnitte bleiben erhalten.");
        var directoryRow = Grid(2, 1); directoryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); directoryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); _directory.Margin = new Padding(0, 2, 7, 2); directoryRow.Controls.Add(_directory, 0, 0); var browse = ActionButton("Wählen …"); browse.Dock = DockStyle.Fill; browse.Margin = Padding.Empty;
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { Description = "Aufnahmeordner für ScreenDash wählen", UseDescriptionForTitle = true, SelectedPath = _directory.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) _directory.Text = dialog.SelectedPath; }; directoryRow.Controls.Add(browse, 1, 0);
        AddField(storageFields, "Aufnahmeordner", directoryRow); AddOption(storageFields, _singleFile, 39); _singleFile.CheckedChanged += (_, _) => _segment.Enabled = !_singleFile.Checked; AddField(storageFields, "Abschnittslänge", WithUnit(_segment, "Minuten · einzeln abspielbare Videodateien")); AddField(storageFields, "Automatisch löschen nach", WithUnit(_retention, "Tagen · nur ungesicherte Abschnitte")); AddField(storageFields, "Speicherlimit", WithUnit(_maxStorage, "GB · älteste ungesicherte Abschnitte zuerst")); AddField(storageFields, "Freie Reserve", WithUnit(_minFree, "GB · bei Platzmangel stoppt die Aufnahme")); AddField(storageFields, "Rückblick sichern", WithUnit(_protectMinutes, "Minuten · für Schaltfläche und Tastenkürzel")); AddWide(storageFields, _quotaEstimate, 48); _retention.ValueChanged += (_, _) => UpdateEstimates(); _maxStorage.ValueChanged += (_, _) => UpdateEstimates();
        var automationFields = SettingsGrid(_settingTabs[3]); AddHeading(automationFields, "Läuft, wenn du es brauchst.", "ScreenDash arbeitet im Hintergrund und bleibt in der Taskleiste erreichbar."); AddOption(automationFields, _startWithWindows, 39); AddOption(automationFields, _recordOnLaunch, 39); AddOption(automationFields, _pauseOnLock, 39); AddOption(automationFields, _minimizeOnRecord, 39); AddOption(automationFields, _notifications, 39); AddOption(automationFields, _autoStopEnabled, 39); AddField(automationFields, "Auto-Stopp nach", WithUnit(_autoStopMinutes, "Minuten Aufnahmezeit · Pausen zählen nicht")); _autoStopEnabled.CheckedChanged += (_, _) => _autoStopMinutes.Enabled = _autoStopEnabled.Checked; _tips.SetToolTip(_autoStopEnabled, "Stoppt die gesamte Sitzung nach der eingestellten Aufnahmezeit. Standardmäßig aus."); AddHelp(automationFields, "Tastenkürzel funktionieren auch bei minimiertem Fenster.\nStrg + Alt + F8   Start / Stopp    ·    F9   Sichern    ·    F10   Pause / Fortsetzen");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 14, 0, 0), Margin = Padding.Empty }; _save.Size = new Size(242, 42); _discard.Size = new Size(130, 42); _save.Click += async (_, _) => await SaveSettingsAsync(); _discard.Click += (_, _) => { LoadSettings(); RefreshDashboard(); _footer.Text = "Änderungen verworfen."; }; actions.Controls.AddRange([_save, _discard]); page.Controls.Add(actions, 0, 3); _settingsPage.Controls.Add(page); ShowSettingTab(0);
        _tips.SetToolTip(_save, "Geänderte Einstellungen übernehmen · Strg + S");
    }

    private void BuildRecordings()
    {
        var page = Grid(1, 6); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); page.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 57)); page.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var filters = Grid(4, 1); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 111)); _search.PlaceholderText = "Suchen · Strg + F"; _search.AccessibleName = "Aufnahmen durchsuchen"; _search.AccessibleDescription = "Dateinamen und persönliche Notizen durchsuchen · Strg + F"; _archiveFilter.AccessibleName = "Aufnahmen nach Schutz filtern"; _dateFilter.AccessibleName = "Aufnahmen nach Zeitraum filtern"; _search.Margin = new Padding(0, 0, 10, 10); _archiveFilter.Items.AddRange(["Alle Aufnahmen", "Nur gesicherte", "Nur ungesicherte"]); _dateFilter.Items.AddRange(["Alle Zeiten", "24 Stunden", "7 Tage"]); _archiveFilter.Margin = new Padding(0, 0, 10, 10); _dateFilter.Margin = new Padding(0, 0, 10, 10); _archiveFilter.SelectedIndex = 0; _dateFilter.SelectedIndex = 0;
        filters.Controls.Add(_search, 0, 0); filters.Controls.Add(_archiveFilter, 1, 0); filters.Controls.Add(_dateFilter, 2, 0); _clearFilters.Dock = DockStyle.Fill; _clearFilters.Margin = new Padding(0, 0, 0, 10); _clearFilters.Font = new Font("Segoe UI", 8.5f); _clearFilters.Click += (_, _) => ResetArchiveFilters(); filters.Controls.Add(_clearFilters, 3, 0); _search.TextChanged += (_, _) => ResetArchivePage(); _archiveFilter.SelectedIndexChanged += (_, _) => ResetArchivePage(); _dateFilter.SelectedIndexChanged += (_, _) => ResetArchivePage(); page.Controls.Add(filters, 0, 0);
        var summary = Grid(4, 1); summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 123)); summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118)); summary.Controls.Add(_recordingsSummary, 0, 0); var refresh = ActionButton("Aktualisieren"); refresh.Font = new Font("Segoe UI", 9); refresh.Dock = DockStyle.Fill; refresh.Margin = new Padding(0, 0, 0, 7); refresh.Click += (_, _) => RefreshRecordings(); _findTime.Dock = _bookmarksSelected.Dock = DockStyle.Fill; _findTime.Font = _bookmarksSelected.Font = new Font("Segoe UI", 8.5f); _findTime.Margin = _bookmarksSelected.Margin = new Padding(0, 0, 7, 7); _findTime.Click += async (_, _) => await FindTimeAsync(); _bookmarksSelected.Click += async (_, _) => await ShowBookmarksAsync(); summary.Controls.Add(_findTime, 1, 0); summary.Controls.Add(_bookmarksSelected, 2, 0); summary.Controls.Add(refresh, 3, 0); page.Controls.Add(summary, 0, 1);
        _recordings.Dock = DockStyle.Fill; _recordings.BackgroundColor = Card; _recordings.BorderStyle = BorderStyle.None; _recordings.AllowUserToAddRows = false; _recordings.AllowUserToDeleteRows = false; _recordings.AllowUserToResizeRows = false; _recordings.RowHeadersVisible = false; _recordings.ReadOnly = true; _recordings.MultiSelect = true; _recordings.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _recordings.AutoGenerateColumns = false; _recordings.EnableHeadersVisualStyles = false; _recordings.ColumnHeadersHeight = 42; _recordings.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; _recordings.GridColor = Line; _recordings.RowTemplate.Height = 47;
        _recordings.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Card, ForeColor = Ink, SelectionBackColor = Cyan, SelectionForeColor = Color.FromArgb(18, 20, 24), Padding = new Padding(10, 0, 0, 0), Font = new Font("Segoe UI", 9.5f) }; _recordings.AlternatingRowsDefaultCellStyle.BackColor = Canvas; _recordings.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Input, ForeColor = Muted, SelectionBackColor = Input, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Padding = new Padding(10, 0, 0, 0) }; _recordings.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; _recordings.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _recordings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "BEGINN", Width = 150 }); _recordings.Columns.Add(new DataGridViewTextBoxColumn { Name = "File", HeaderText = "AUFNAHME / NOTIZ", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 210 }); _recordings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Size", HeaderText = "GRÖSSE", Width = 88 }); _recordings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Protect", HeaderText = "SCHUTZ", Width = 110 }); _recordings.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "STATUS", Width = 105 });
        foreach (DataGridViewColumn column in _recordings.Columns) { column.SortMode = DataGridViewColumnSortMode.Programmatic; column.HeaderCell.ToolTipText = "Nach dieser Spalte sortieren; erneut klicken, um die Reihenfolge umzukehren."; }
        _recordings.ColumnHeaderMouseClick += (_, e) => { if (e.ColumnIndex >= 0) SortArchive(_recordings.Columns[e.ColumnIndex].Name); };
        _recordings.SelectionChanged += (_, _) => { if (!_refreshingArchive) UpdateRecordingActions(); }; _recordings.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) PlaySelected(); }; _recordings.CellMouseDown += (_, e) => { if (e.Button != MouseButtons.Right || e.RowIndex < 0) return; var row = _recordings.Rows[e.RowIndex]; if (!row.Selected) { _recordings.ClearSelection(); row.Selected = true; } _recordings.CurrentCell = row.Cells[0]; };
        AddArchiveMenuItem("Play", "Abspielen", "Eingabe", (_, _) => PlaySelected()); AddArchiveMenuItem("Reveal", "Im Explorer anzeigen", "", (_, _) => RevealSelected()); AddArchiveMenuItem("Export", "Als MP4 exportieren …", "Strg + E", async (_, _) => await ExportSelectedAsync()); AddArchiveMenuItem("Clip", "Kurzen Clip exportieren …", "Strg + Umschalt + E", async (_, _) => await ExportClipSelectedAsync()); AddArchiveMenuItem("Label", "Notiz ändern …", "F2", async (_, _) => await LabelSelectedAsync()); _archiveMenu.Items.Add(new ToolStripSeparator()); AddArchiveMenuItem("Protect", "Auswahl sichern", "Strg + G", async (_, _) => await ProtectSelectedAsync(true)); AddArchiveMenuItem("Release", "Auswahl freigeben", "Strg + Umschalt + G", async (_, _) => await ProtectSelectedAsync(false)); AddArchiveMenuItem("Delete", "Auswahl löschen …", "Entf", async (_, _) => await DeleteSelectedAsync()); _archiveMenu.Opening += (_, e) => { UpdateRecordingActions(); e.Cancel = _uiBusy || SelectedRecordings().Length == 0; }; _recordings.ContextMenuStrip = _archiveMenu;
        var tableHost = Surface(new Padding(1)); tableHost.Margin = Padding.Empty; tableHost.Controls.Add(_recordings); _emptyArchive.Dock = DockStyle.Fill; _emptyArchive.TextAlign = ContentAlignment.MiddleCenter; _emptyArchive.BackColor = Card; tableHost.Controls.Add(_emptyArchive); page.Controls.Add(tableHost, 0, 2);
        var pager = Grid(3, 1); pager.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pager.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); pager.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); pager.Controls.Add(_archivePageLabel, 0, 0); _previousArchivePage.Dock = _nextArchivePage.Dock = DockStyle.Fill; _previousArchivePage.Font = _nextArchivePage.Font = new Font("Segoe UI", 9); _previousArchivePage.Margin = new Padding(0, 6, 7, 1); _nextArchivePage.Margin = new Padding(0, 6, 0, 1); _previousArchivePage.Click += (_, _) => ChangeArchivePage(-1); _nextArchivePage.Click += (_, _) => ChangeArchivePage(1); pager.Controls.Add(_previousArchivePage, 1, 0); pager.Controls.Add(_nextArchivePage, 2, 0); page.Controls.Add(pager, 0, 3);
        var actions = Grid(7, 1); actions.Padding = new Padding(0, 12, 0, 4); float[] actionWidths = [12, 12, 13, 20, 17, 13, 13]; Button[] archiveButtons = [_play, _protectSelected, _releaseSelected, _export, _clip, _labelSelected, _delete]; for (int i = 0; i < archiveButtons.Length; i++) { actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, actionWidths[i])); var button = archiveButtons[i]; button.Dock = DockStyle.Fill; button.Font = new Font("Segoe UI", 9); button.Margin = new Padding(0, 0, i == archiveButtons.Length - 1 ? 0 : 7, 0); actions.Controls.Add(button, i, 0); } _delete.ForeColor = Ink; ((ThemeButton)_clip).AccentBorder = true;
        _play.Click += (_, _) => PlaySelected(); _protectSelected.Click += async (_, _) => await ProtectSelectedAsync(true); _releaseSelected.Click += async (_, _) => await ProtectSelectedAsync(false); _export.Click += async (_, _) => await ExportSelectedAsync(); _clip.Click += async (_, _) => await ExportClipSelectedAsync(); _labelSelected.Click += async (_, _) => await LabelSelectedAsync(); _delete.Click += async (_, _) => await DeleteSelectedAsync(); page.Controls.Add(actions, 0, 4);
        var progress = _archiveProgressLayout; progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); progress.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0)); progress.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0)); progress.Controls.Add(_archiveProgress, 0, 0); _exportProgress.Margin = new Padding(0, 6, 10, 6); progress.Controls.Add(_exportProgress, 1, 0); _cancelExport.Dock = DockStyle.Fill; _cancelExport.Font = new Font("Segoe UI", 8.5f); _cancelExport.Margin = new Padding(0, 2, 0, 2); _cancelExport.Visible = false; _cancelExport.Click += (_, _) => CancelExport(); progress.Controls.Add(_cancelExport, 2, 0); page.Controls.Add(progress, 0, 5); _recordingsPage.Controls.Add(page); _tips.SetToolTip(_labelSelected, "Eine persönliche Notiz für den ausgewählten Abschnitt hinterlegen · F2"); _tips.SetToolTip(_delete, "Ausgewählte abgeschlossene Aufnahmen dauerhaft löschen; aktive Abschnitte bleiben erhalten · Entf"); _tips.SetToolTip(_clearFilters, "Suchtext und Zeitraum zurücksetzen · Strg + Umschalt + F"); _tips.SetToolTip(_clip, "Nur einen Moment aus einer abgeschlossenen Aufnahme als MP4 kopieren · Strg + Umschalt + E");
    }

    private void ShowPage(int page)
    {
        bool changed = _page != page;
        _content.SuspendLayout();
        _page = page; _dashboard.Visible = page == 0; _settingsPage.Visible = page == 1; _recordingsPage.Visible = page == 2; _toolsPage.Visible = page == 3;
        (_pageTitle.Text, _pageSubtitle.Text) = page switch { 1 => ("Einstellungen", "Passe deine Bildschirm-Dashcam an deinen Alltag an."), 2 => ("Deine Aufnahmen", "Lesezeichen finden und Momente als Clip teilen."), 3 => ("Werkzeuge", "Deine Profile, Spielerkennung und Updates an einem Ort."), _ => ("Aufnahme", "Dein Bildschirm. Als sicherer Rückblick.") };
        for (int i = 0; i < _nav.Length; i++) SetNavigation(_nav[i], i == page);
        _content.ResumeLayout(true);
        if (changed) BeginPageTransition();
        if (page == 2) RefreshRecordings(); if (page == 3) _ = ReloadProfilesAsync();
    }
    private void ShowSettingTab(int tab) { for (int i = 0; i < _settingTabs.Length; i++) { _settingTabs[i].Visible = i == tab; SetNavigation(_settingNav[i], i == tab); } }
    private static void SetNavigation(Button button, bool active)
    {
        var themed = (ThemeButton)button;
        bool sidebar = themed.LeftAligned;
        button.BackColor = active ? Cyan : sidebar ? Sidebar : Input;
        button.ForeColor = active ? Color.FromArgb(18, 20, 24) : sidebar ? Color.FromArgb(193, 197, 204) : Muted;
        button.FlatAppearance.MouseOverBackColor = active ? Color.FromArgb(233, 237, 242) : Color.FromArgb(47, 51, 57);
        button.FlatAppearance.MouseDownBackColor = active ? Color.FromArgb(187, 194, 203) : Color.FromArgb(61, 66, 74);
        themed.Active = active; themed.AccentBorder = active; button.Invalidate();
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try { var s = _controller.Settings; LoadDevices(false); LoadFeatureSettings(s); _audioEnabled.Checked = s.AudioEnabled; _cursor.Checked = s.CaptureCursor; _software.Checked = s.AllowSoftwareEncoding; _pauseOnLock.Checked = s.PauseOnLock; _recordOnLaunch.Checked = s.RecordOnLaunch; _startWithWindows.Checked = s.StartWithWindows; _notifications.Checked = s.ShowNotifications; _minimizeOnRecord.Checked = s.MinimizeOnRecord; _autoStopEnabled.Checked = s.AutoStopMinutes > 0; SetNumber(_autoStopMinutes, s.AutoStopMinutes > 0 ? s.AutoStopMinutes : 60); _autoStopMinutes.Enabled = _autoStopEnabled.Checked; _directory.Text = s.RecordingDirectory; _singleFile.Checked = s.SingleFile; _segment.Enabled = !s.SingleFile; SetNumber(_segment, s.SegmentMinutes); SetNumber(_retention, s.RetentionDays); SetNumber(_maxStorage, s.MaxStorageGb); SetNumber(_minFree, s.MinFreeSpaceGb); SetNumber(_protectMinutes, s.ProtectMinutes); SetNumber(_customFps, s.Fps); SetNumber(_customBitrate, s.VideoBitrateKbps); _profile.SelectedIndex = (s.Fps, s.VideoBitrateKbps) switch { (10, 1500) => 0, (15, 3000) => 1, (30, 6000) => 2, (30, 10000) => 3, _ => 4 }; _encoderChoice.SelectedIndex = s.EncoderMode.ToUpperInvariant() switch { "NVIDIA" => 1, "AMD" => 2, "INTEL" => 3, "SOFTWARE" => 4, _ => 0 }; _audio.Enabled = _audioEnabled.Checked; }
        finally { _loadingSettings = false; } _settingsBaseline = SettingsFingerprint(); _settingsDirty = false; UpdateEstimates();
    }
    private void LoadDevices(bool preserveSelection = true)
    {
        bool previousLoading = _loadingSettings; _loadingSettings = true;
        try
        {
        string displayId = preserveSelection ? (_display.SelectedItem as DisplaySource)?.Id ?? _controller.Settings.DisplayId : _controller.Settings.DisplayId; string audioId = preserveSelection ? (_audio.SelectedItem as AudioSource)?.Id ?? _controller.Settings.AudioDeviceId : _controller.Settings.AudioDeviceId;
        var previousDisplay = _display.SelectedItem as DisplaySource; LoadMicrophones(preserveSelection);
        _display.Items.Clear(); foreach (var source in _controller.Displays) _display.Items.Add(source); _display.SelectedIndex = FindSource(_display, item => ((DisplaySource)item).Id == displayId); _audio.Items.Clear(); foreach (var source in _controller.AudioDevices) _audio.Items.Add(source); _audio.SelectedIndex = FindSource(_audio, item => ((AudioSource)item).Id == audioId);
        if (!string.IsNullOrEmpty(displayId) && !_controller.Displays.Any(d => d.Id == displayId))
        { _display.Items.Add(new DisplaySource(displayId, (previousDisplay?.Name ?? "Gespeicherter Bildschirm") + " · Nicht verbunden", -1, -1, 0, 0, 0, 0)); _display.SelectedIndex = _display.Items.Count - 1; }
        if (!string.IsNullOrEmpty(audioId) && !_controller.AudioDevices.Any(d => d.Id == audioId))
        { _audio.Items.Add(new AudioSource(audioId, "Gespeicherte Audioausgabe · Nicht verfügbar")); _audio.SelectedIndex = _audio.Items.Count - 1; }
        }
        finally { _loadingSettings = previousLoading; }
        if (!_loadingSettings && _settingsBaseline.Length > 0) UpdateDirtyState();
    }
    private static int FindSource(ComboBox choice, Func<object, bool> predicate) { for (int i = 0; i < choice.Items.Count; i++) if (choice.Items[i] is { } source && predicate(source)) return i; return choice.Items.Count > 0 ? 0 : -1; }
    private void ApplyProfile() { if (_loadingSettings || _profile.SelectedIndex is < 0 or > 3) return; _updatingProfile = true; try { (int fps, int bitrate) = _profile.SelectedIndex switch { 0 => (10, 1500), 1 => (15, 3000), 2 => (30, 6000), _ => (30, 10000) }; SetNumber(_customFps, fps); SetNumber(_customBitrate, bitrate); } finally { _updatingProfile = false; } UpdateEstimates(); }
    private void CustomQualityChanged() { if (_loadingSettings || _updatingProfile) return; _profile.SelectedIndex = 4; UpdateEstimates(); }
    private void UpdateEstimates()
    {
        if (_loadingSettings) return; var draft = _controller.Settings.Clone(); draft.Fps = (int)_customFps.Value; draft.VideoBitrateKbps = (int)_customBitrate.Value; draft.AudioEnabled = _audioEnabled.Checked; draft.MaxStorageGb = (int)_maxStorage.Value; draft.RetentionDays = (int)_retention.Value;
        var estimate = RecordingEstimates.Calculate(draft);
        _qualityEstimate.Text = $"Geschätzt: {estimate.GiBPerHour:0.0} GB pro Aufnahmestunde.\nDie tatsächliche Größe hängt vom Encoder und Bildinhalt ab.";
        _quotaEstimate.Text = $"Das Limit reicht ungefähr für {FormatHours(estimate.HoursAtLimit)} Aufnahme.\n{draft.RetentionDays} Tage gelten zusätzlich; dauerhafte Aufnahme kann früher gelöscht werden."; _quotaEstimate.ForeColor = estimate.FullRetentionFits ? Cyan : Amber;
    }
    private static string FormatHours(double hours) => hours >= 48 ? $"{hours / 24:0.0} Tage" : $"{hours:0.0} Stunden";
    private AppSettings ReadSettings()
    {
        if (_display.SelectedItem is not DisplaySource display || display.AdapterIndex < 0) throw new InvalidOperationException("Es ist kein Bildschirm verfügbar. Bitte Geräte aktualisieren."); if (_audioEnabled.Checked && _audio.SelectedItem is not AudioSource) throw new InvalidOperationException("Bitte ein Audioausgabegerät wählen oder Systemton deaktivieren."); if (string.IsNullOrWhiteSpace(_directory.Text)) throw new InvalidOperationException("Bitte einen Aufnahmeordner wählen.");
        var s = _controller.Settings.Clone(); s.DisplayId = display.Id; s.AudioDeviceId = (_audio.SelectedItem as AudioSource)?.Id ?? ""; s.AudioEnabled = _audioEnabled.Checked; s.CaptureCursor = _cursor.Checked; s.AllowSoftwareEncoding = _software.Checked; s.PauseOnLock = _pauseOnLock.Checked; s.RecordOnLaunch = _recordOnLaunch.Checked; s.StartWithWindows = _startWithWindows.Checked; s.ShowNotifications = _notifications.Checked; s.MinimizeOnRecord = _minimizeOnRecord.Checked; s.AutoStopMinutes = _autoStopEnabled.Checked ? (int)_autoStopMinutes.Value : 0; s.RecordingDirectory = _directory.Text.Trim(); s.SingleFile = _singleFile.Checked; s.SegmentMinutes = (int)_segment.Value; s.RetentionDays = (int)_retention.Value; s.MaxStorageGb = (int)_maxStorage.Value; s.MinFreeSpaceGb = (int)_minFree.Value; s.ProtectMinutes = (int)_protectMinutes.Value; s.Fps = (int)_customFps.Value; s.VideoBitrateKbps = (int)_customBitrate.Value; ReadFeatureSettings(s); s.EncoderMode = _encoderChoice.SelectedIndex switch { 1 => "NVIDIA", 2 => "AMD", 3 => "Intel", 4 => "Software", _ => "Auto" }; if (s.EncoderMode == "Software" && !s.AllowSoftwareEncoding) throw new InvalidOperationException("Für den Software-Encoder bitte „CPU-Kodierung als Ausweichlösung erlauben“ aktivieren."); return s;
    }

    private Task TogglePauseAsync() => _controller.Status.Paused ? _controller.ResumeAsync() : _controller.PauseAsync();
    private void ControllerChanged()
    {
        if (IsDisposed || !IsHandleCreated || Interlocked.Exchange(ref _dashboardUpdateQueued, 1) != 0) return;
        OnUi(() => { Interlocked.Exchange(ref _dashboardUpdateQueued, 0); if (!IsDisposed) RefreshDashboard(); });
    }
    private void ControllerNotice(string message) => OnUi(() => { _footer.Text = message; Notify(message); });
    private void Notify(string message, ToolTipIcon icon = ToolTipIcon.Info) { if (_controller.Settings.ShowNotifications) _tray.ShowBalloonTip(4500, "ScreenDash", message, icon); }
    private void OnUi(Action action) { if (IsDisposed || Disposing) return; if (InvokeRequired) { try { BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) action(); })); } catch (InvalidOperationException) { } return; } action(); }
    private void RefreshDashboard()
    {
        var status = _controller.Status; var s = _controller.Settings; bool busy = status.Busy || _uiBusy || _stopping || _exitInProgress; bool armed = status.Armed || status.Recording || status.Paused; bool paused = status.Paused;
        bool idleDraft = _settingsDirty && !armed && !busy;
        _nav[1].Text = _settingsDirty ? "Einstellungen •" : "Einstellungen"; _nav[1].AccessibleName = _settingsDirty ? "Einstellungen, ungespeicherte Änderungen" : "Einstellungen";
        _headerState.Text = status.Recording ? "AUFNAHME LÄUFT" : busy ? "WIRD BEARBEITET" : paused ? "PAUSIERT" : idleDraft ? "ÄNDERUNGEN OFFEN" : "BEREIT"; _headerState.ForeColor = status.Recording ? Cyan : paused || idleDraft ? Amber : Muted;
        _recordingState.Text = status.Recording ? "Dein Rückblick läuft" : paused ? "Aufnahme pausiert" : busy ? "Einen Moment …" : "Bereit für deinen Rückblick"; _recordingMessage.Text = idleDraft ? "Ungespeicherte Änderungen · Start nutzt gespeicherte Einstellungen." : !string.IsNullOrWhiteSpace(status.PauseReason) && paused ? status.PauseReason : status.Message; _recordingMessage.ForeColor = idleDraft ? Amber : Muted; _elapsed.Text = $"{(int)status.Elapsed.TotalHours:00}:{status.Elapsed.Minutes:00}:{status.Elapsed.Seconds:00}";
        _autoStopCountdown.Text = status.Remaining is { } remaining ? $"{(paused ? "Pausiert · " : "Stopp in ")}{FormatDuration(remaining)}" : s.AutoStopMinutes > 0 ? $"Auto-Stopp: {s.AutoStopMinutes} Min." : "Auto-Stopp aus"; _autoStopCountdown.ForeColor = status.Remaining is { TotalSeconds: <= 60 } ? Amber : Muted;
        _encoder.Text = string.IsNullOrWhiteSpace(status.Encoder) ? "GPU bevorzugt" : status.Encoder.Replace("NVIDIA ", "").Replace("Intel ", ""); _cpu.Text = $"{Math.Max(0, status.CpuPercent):0.0} %"; _memory.Text = $"{status.MemoryBytes / 1024d / 1024d:0} MB"; _fps.Text = $"{(status.Recording ? status.Fps : s.Fps):0.#} FPS";
        bool softwareEncoder = status.Encoder.Contains("Software", StringComparison.OrdinalIgnoreCase) || status.Encoder.Contains("libx264", StringComparison.OrdinalIgnoreCase);
        bool hardwareEncoder = status.Encoder.Contains("NVENC", StringComparison.OrdinalIgnoreCase) || status.Encoder.Contains("AMF", StringComparison.OrdinalIgnoreCase) || status.Encoder.Contains("Quick Sync", StringComparison.OrdinalIgnoreCase) || status.Encoder.Contains("h264_qsv", StringComparison.OrdinalIgnoreCase);
        _encoderDetail.Text = softwareEncoder ? "Software · H.264" : hardwareEncoder ? "Hardware · H.264" : string.IsNullOrWhiteSpace(status.Encoder) ? "H.264 · Startprüfung" : "H.264";
        _tips.SetToolTip(_encoderDetail, softwareEncoder && !s.EncoderMode.Equals("Software", StringComparison.OrdinalIgnoreCase) ? "CPU-Kodierung als ausdrücklich erlaubte Ausweichlösung." : softwareEncoder ? "Die CPU übernimmt die Kodierung." : hardwareEncoder ? "Die GPU übernimmt die Kodierung." : "Der Encoder wird beim Aufnahmestart geprüft.");
        string audioSummary = (s.AudioEnabled, s.MicrophoneEnabled) switch { (true, true) => "Systemton + Mikrofon", (true, false) => "Systemton an", (false, true) => "Nur Mikrofon", _ => "Ohne Ton" };
        var display = string.IsNullOrEmpty(s.DisplayId) ? _controller.Displays.FirstOrDefault() : _controller.Displays.FirstOrDefault(d => d.Id == s.DisplayId); _source.Text = $"{display?.Name ?? "Kein Bildschirm"}   ·   {audioSummary}   ·   {(s.SingleFile ? "Eine durchgehende Datei" : $"{s.SegmentMinutes} Min. pro Abschnitt")}";
        _sound.Text = status.AudioWarning.Length > 0 ? "⚠ " + status.AudioWarning : !status.Recording ? "Tonprüfung unter Einstellungen → Quelle" : $"Spielton {(s.AudioEnabled ? $"{status.AudioPeak * 100:0} %" : "aus")}   ·   Mikrofon {(s.MicrophoneEnabled ? $"{status.MicrophonePeak * 100:0} %" : "aus")}"; _sound.ForeColor = status.AudioWarning.Length > 0 ? Amber : Cyan;
        _tips.SetToolTip(_sound, status.AudioWarning.Length > 0 ? status.AudioWarning : $"Spielton: {status.AudioDevice}\nMikrofon: {status.MicrophoneDevice}");
        _toggle.Text = _stopping ? "Wird gestoppt …" : armed ? "Aufnahme stoppen" : "Aufnahme starten"; ((ThemeButton)_toggle).IconKind = armed ? AppGlyph.Stop : AppGlyph.Record; _toggle.Enabled = !_stopping && !_exitInProgress && (armed || !busy); _pause.Text = paused ? "Fortsetzen" : "Pause"; ((ThemeButton)_pause).IconKind = paused ? AppGlyph.Play : AppGlyph.Pause; _pause.Enabled = !busy && armed; _protect.Text = s.SingleFile ? "Aufnahme sichern" : $"{s.ProtectMinutes} Min. sichern"; _tips.SetToolTip(_protect, s.SingleFile ? "Sichert die gesamte laufende Datei sowie Aufnahmen aus den letzten eingestellten Minuten." : "Sichert ganze Abschnitte aus dem gewählten Zeitraum."); _protect.Enabled = !busy;
        _trayToggle.Text = _stopping ? "Aufnahme wird gestoppt …" : armed ? "Aufnahme stoppen" : "Aufnahme starten"; _trayToggle.Enabled = _toggle.Enabled; _trayPause.Text = paused ? "Aufnahme fortsetzen" : "Aufnahme pausieren"; _trayPause.Enabled = _pause.Enabled; _trayProtect.Text = s.SingleFile ? "Aufnahme sichern" : $"Letzte {s.ProtectMinutes} Min. sichern"; _trayProtect.Enabled = !busy; _tray.Text = status.Recording ? "ScreenDash · Aufnahme läuft" : paused ? "ScreenDash · pausiert" : "ScreenDash · " + (busy ? "arbeitet" : "bereit");
        _settingsFields.Enabled = !armed && !busy; _save.Enabled = !armed && !busy && _settingsDirty; _discard.Enabled = !busy && _settingsDirty; _settingsHint.Text = armed ? "Sitzung aktiv. Stoppe die Aufnahme, um diese Einstellungen zu ändern." : _settingsDirty ? "● Ungespeicherte Änderungen · Strg + S zum Speichern" : "Einstellungen sind gespeichert."; _settingsHint.ForeColor = armed || _settingsDirty ? Amber : Muted;
        if (status.Storage is { } storage) { _storage.Text = $"{FormatBytes(storage.TotalBytes)} von {s.MaxStorageGb} GB belegt"; _storageDetail.Text = $"{FormatBytes(storage.FreeBytes)} frei auf dem Laufwerk   ·   {FormatBytes(storage.ProtectedBytes)} gesichert   ·   {s.MinFreeSpaceGb} GB Reserve"; _storageMeter.Fraction = Math.Clamp(storage.TotalBytes / (s.MaxStorageGb * 1024d * 1024d * 1024d), 0, 1); _storageMeter.ProtectedFraction = Math.Clamp(storage.ProtectedBytes / (s.MaxStorageGb * 1024d * 1024d * 1024d), 0, 1); _storageMeter.Warning = !storage.CanRecord; var estimate = RecordingEstimates.Calculate(s, storage.ProtectedBytes, storage.FreeBytes); _storageEstimate.Text = !storage.CanRecord ? storage.Message : $"Ungesicherte Aufnahmen: max. {s.RetentionDays} Tage   ·   Bei Daueraufnahme ungefähr {FormatHours(estimate.EffectiveRetentionHours)} im Rückblick"; _storageEstimate.ForeColor = storage.CanRecord ? Cyan : Amber; }
        if (status.Recording && !_wasRecording && s.MinimizeOnRecord && Visible) BeginInvoke((Action)MinimizeToTaskbar); _wasRecording = status.Recording;
        if (Visible && WindowState != FormWindowState.Minimized && _page == 2 && !_archiveLoading && Stopwatch.GetElapsedTime(_lastRecordingsRefresh).TotalSeconds >= 10 && !_uiBusy) RefreshRecordings(); UpdateRecordingActions(); RefreshFeatureActions();
    }

    private void RefreshRecordings(bool reload = true)
    {
        if (reload) { _ = ReloadArchiveAsync(); return; }
        if (_refreshingArchive || _recordings.Columns.Count == 0 || IsDisposed) return; _refreshingArchive = true;
        try
        {
            var selected = SelectedRecordings().Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase); int scroll = _recordings.FirstDisplayedScrollingRowIndex; string query = _search.Text.Trim(); DateTime cutoff = _dateFilter.SelectedIndex switch { 1 => DateTime.UtcNow.AddHours(-24), 2 => DateTime.UtcNow.AddDays(-7), _ => DateTime.MinValue }; var all = _archiveCache;
            var filtered = all.Where(r => r.StartedUtc >= cutoff && (_archiveFilter.SelectedIndex != 1 || r.Protected) && (_archiveFilter.SelectedIndex != 2 || !r.Protected) && (query.Length == 0 || r.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Label.Contains(query, StringComparison.OrdinalIgnoreCase)));
            Func<RecordingInfo, IComparable> sortKey = _archiveSortColumn switch { "Size" => r => r.Bytes, "File" => r => string.IsNullOrWhiteSpace(r.Label) ? r.FileName : r.Label, "Protect" => r => r.Protected, "State" => r => r.Active, _ => r => r.StartedUtc };
            var matches = (_archiveSortDescending ? filtered.OrderByDescending(sortKey) : filtered.OrderBy(sortKey)).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            _archivePageCount = Math.Max(1, (matches.Length + ArchivePageSize - 1) / ArchivePageSize); _archivePage = Math.Clamp(_archivePage, 0, _archivePageCount - 1);
            var recordings = matches.Skip(_archivePage * ArchivePageSize).Take(ArchivePageSize).ToArray();
            bool sameRows = recordings.Length == _recordings.Rows.Count && recordings.Select((item, i) => (_recordings.Rows[i].Tag as RecordingInfo)?.Path == item.Path).All(same => same);
            _recordings.SuspendLayout(); if (!sameRows) _recordings.Rows.Clear();
            for (int i = 0; i < recordings.Length; i++)
            {
                var item = recordings[i]; string title = string.IsNullOrWhiteSpace(item.Label) ? item.FileName : item.Label;
                if (!sameRows) _recordings.Rows.Add(); var row = _recordings.Rows[i];
                if (Equals(row.Tag, item)) continue;
                object[] values = [item.StartedUtc.ToLocalTime().ToString("dd.MM.yy  HH:mm"), title, FormatBytes(item.Bytes), item.Protected ? "◇  Gesichert" : "Automatisch", item.Active ? "●  Läuft" : "Fertig"];
                for (int column = 0; column < values.Length; column++) if (!Equals(row.Cells[column].Value, values[column])) row.Cells[column].Value = values[column];
                row.Tag = item; row.Cells[1].ToolTipText = string.IsNullOrWhiteSpace(item.Label) ? item.FileName : item.Label + "\n" + item.FileName; row.Cells[3].Style.ForeColor = item.Protected ? Violet : Ink; row.Cells[4].Style.ForeColor = item.Active ? Cyan : Ink;
            }
            if (!sameRows) { _recordings.ClearSelection(); foreach (DataGridViewRow row in _recordings.Rows) if (row.Tag is RecordingInfo item && selected.Contains(item.Path)) row.Selected = true; if (scroll >= 0 && _recordings.Rows.Count > 0) _recordings.FirstDisplayedScrollingRowIndex = Math.Min(scroll, _recordings.Rows.Count - 1); } _recordings.ResumeLayout();
            _emptyArchive.Visible = recordings.Length == 0; if (_emptyArchive.Visible) { _emptyArchive.Text = all.Count == 0 ? "Noch kein Rückblick vorhanden\n\nStarte eine Aufnahme. Deine Abschnitte erscheinen automatisch hier." : "Keine passenden Aufnahmen\n\nÄndere den Suchtext oder die Filter."; _emptyArchive.BringToFront(); }
            _recordingsSummary.Text = $"{matches.Length} von {all.Count} Aufnahmen   ·   {FormatBytes(matches.Sum(r => r.Bytes))}   ·   {matches.Count(r => r.Protected)} gesichert";
            _archivePageLabel.Text = $"Seite {_archivePage + 1} von {_archivePageCount}   ·   {(matches.Length == 0 ? "0 Treffer" : $"{_archivePage * ArchivePageSize + 1}–{_archivePage * ArchivePageSize + recordings.Length} von {matches.Length} Treffern")}";
            foreach (DataGridViewColumn column in _recordings.Columns) column.HeaderCell.SortGlyphDirection = column.Name == _archiveSortColumn ? (_archiveSortDescending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
        }
        catch (Exception ex) { ShowError(ex); } finally { _refreshingArchive = false; _recordings.ResumeLayout(); UpdateRecordingActions(); }
    }
    private async Task ReloadArchiveAsync()
    {
        if (IsDisposed || _exitInProgress) return;
        _lastRecordingsRefresh = Stopwatch.GetTimestamp();
        if (_archiveLoading) { _archiveReloadPending = true; return; }
        _archiveLoading = true; _archiveLoadError = ""; UpdateRecordingActions();
        try
        {
            do
            {
                _archiveReloadPending = false;
                var snapshot = await _controller.GetRecordingsAsync(_archiveReloadCancellation.Token);
                if (IsDisposed || _exitInProgress) return;
                _archiveCache = snapshot; RefreshRecordings(false);
            } while (_archiveReloadPending);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) { _archiveLoadError = "Aktualisieren fehlgeschlagen: " + ex.Message; _footer.Text = _archiveLoadError; } }
        finally { _archiveLoading = false; if (!IsDisposed) UpdateRecordingActions(); }
    }
    private void ResetArchivePage() { _archivePage = 0; RefreshRecordings(false); }
    private void ChangeArchivePage(int change) { if (_uiBusy || _exitInProgress) return; int target = Math.Clamp(_archivePage + change, 0, _archivePageCount - 1); if (target == _archivePage) return; _archivePage = target; RefreshRecordings(false); if (_recordings.Rows.Count > 0) _recordings.FirstDisplayedScrollingRowIndex = 0; }
    private void ResetArchiveFilters() { _search.Clear(); _archiveFilter.SelectedIndex = 0; _dateFilter.SelectedIndex = 0; ResetArchivePage(); }
    private void SortArchive(string column) { _archiveSortDescending = _archiveSortColumn == column ? !_archiveSortDescending : column is "Time" or "Size"; _archiveSortColumn = column; ResetArchivePage(); }
    private void AddArchiveMenuItem(string key, string text, string shortcut, EventHandler handler)
    { var item = new ToolStripMenuItem(text, null, handler) { Name = key, ShortcutKeyDisplayString = shortcut }; _archiveMenu.Items.Add(item); }
    private RecordingInfo[] SelectedRecordings() => _recordings.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<RecordingInfo>().ToArray();
    private RecordingInfo? SelectedRecording() => SelectedRecordings() is { Length: 1 } items ? items[0] : null;
    private void UpdateRecordingActions()
    {
        var items = SelectedRecordings(); var item = items.Length == 1 ? items[0] : null; bool available = !_uiBusy && !_stopping && !_exporting && !_controller.Status.Busy && !_exitInProgress && items.Length > 0;
        _play.Enabled = available && item is { Active: false }; _export.Enabled = _clip.Enabled = available && item is { Active: false }; _labelSelected.Enabled = available && item is not null; _protectSelected.Enabled = available && items.Any(i => !i.Protected); _releaseSelected.Enabled = available && items.Any(i => i.Protected); _delete.Enabled = available && items.Any(i => !i.Active); _search.Enabled = !_uiBusy; _archiveFilter.Enabled = !_uiBusy; _dateFilter.Enabled = !_uiBusy; _recordings.Enabled = !_uiBusy; _previousArchivePage.Enabled = !_uiBusy && !_exitInProgress && _archivePage > 0; _nextArchivePage.Enabled = !_uiBusy && !_exitInProgress && _archivePage < _archivePageCount - 1;
        foreach (var pair in new[] { ("Play", _play.Enabled), ("Reveal", available && item is not null), ("Export", _export.Enabled), ("Clip", _clip.Enabled), ("Label", _labelSelected.Enabled), ("Protect", _protectSelected.Enabled), ("Release", _releaseSelected.Enabled), ("Delete", _delete.Enabled) }) if (_archiveMenu.Items[pair.Item1] is { } command) command.Enabled = pair.Item2;
        RefreshFeatureActions();
        if (!_exporting)
        {
            _archiveProgress.Text = _uiBusy ? "Auswahl wird bearbeitet …" : _archiveLoading ? "Aufnahmen werden im Hintergrund aktualisiert …" : _archiveLoadError.Length > 0 ? _archiveLoadError
                : item is { Active: true } ? "Die Datei läuft noch · für Wiedergabe oder Export pausieren oder stoppen."
                : items.Length > 1 ? $"{items.Length} ausgewählt · für Wiedergabe und Export eine Aufnahme auswählen."
                : items.Length == 1 ? $"1 ausgewählt   ·   {FormatBytes(items[0].Bytes)}"
                : "Strg + F: Suchen   ·   F2: Benennen   ·   Strg + E: Exportieren";
        }
    }
    private void PlaySelected() { if (SelectedRecording() is not { Active: false } item || _uiBusy) return; RunSync(() => Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true })); }
    private void RevealSelected() { if (SelectedRecording() is not { } item || _uiBusy) return; RunSync(() => Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "/select,\"" + item.Path + "\"" })); }
    private async Task ProtectSelectedAsync(bool protect)
    {
        var items = SelectedRecordings().Where(i => i.Protected != protect).ToArray(); if (items.Length == 0) return;
        await RunActionAsync(async () => { await _controller.SetProtectedManyAsync(items.Select(i => i.Path).ToArray(), protect); _footer.Text = $"{items.Length} Abschnitte {(protect ? "gesichert" : "für automatische Bereinigung freigegeben")}."; });
    }
    private async Task DeleteSelectedAsync()
    {
        var items = SelectedRecordings().Where(i => !i.Active).ToArray(); if (items.Length == 0 || _uiBusy) return; int protectedCount = items.Count(i => i.Protected); string warning = protectedCount > 0 ? $"\n\nDarunter sind {protectedCount} gesicherte Abschnitte. Auch diese werden gelöscht." : "";
        if (MessageBox.Show(this, $"{items.Length} abgeschlossene Aufnahmen ({FormatBytes(items.Sum(i => i.Bytes))}) dauerhaft löschen?{warning}", "Auswahl löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await RunActionAsync(async () => { int count = await _controller.DeleteRecordingsAsync(items.Select(i => i.Path).ToArray(), protectedCount > 0); _footer.Text = $"{count} Aufnahmen gelöscht."; });
    }
    private async Task LabelSelectedAsync()
    {
        if (SelectedRecording() is not { } item || _uiBusy) return;
        using var dialog = new Form { Text = "Aufnahme benennen", ClientSize = new Size(470, 186), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = Canvas, ForeColor = Ink, Font = Font, ShowInTaskbar = false }; var layout = Grid(1, 3); layout.Padding = new Padding(22); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.Controls.Add(TextLabel("Eine Notiz hilft dir, diesen Moment wiederzufinden.", 9, Muted), 0, 0);
        var entry = Entry(); entry.Text = item.Label; entry.MaxLength = 120; entry.Margin = new Padding(0, 0, 0, 10); layout.Controls.Add(entry, 0, 1); var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty }; var save = ActionButton("Speichern", true); save.Size = new Size(125, 37); save.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.Size = new Size(120, 37); cancel.DialogResult = DialogResult.Cancel; actions.Controls.AddRange([save, cancel]); layout.Controls.Add(actions, 0, 2); dialog.Controls.Add(layout); dialog.AcceptButton = save; dialog.CancelButton = cancel; dialog.Shown += (_, _) => { entry.Focus(); entry.SelectAll(); };
        if (dialog.ShowDialog(this) == DialogResult.OK) await RunActionAsync(() => _controller.SetLabelAsync(item.Path, entry.Text.Trim()));
    }
    private async Task ExportSelectedAsync()
    {
        if (SelectedRecording() is not { Active: false } item || _uiBusy || _exporting || _exitInProgress) return;
        string? destination = ChooseExportDestination(item, false); if (destination is null) return;
        await RunArchiveExportAsync(token => _controller.ExportAsync(item.Path, destination, token), "MP4-Export läuft", () => "MP4 exportiert: " + Path.GetFileName(destination));
    }
    private async Task ExportClipSelectedAsync()
    {
        if (SelectedRecording() is not { Active: false } item || _uiBusy || _exporting || _exitInProgress) return;
        string? destination = null;
        await RunArchiveExportAsync(async token =>
        {
            _archiveProgress.Text = "Aufnahmedauer wird ermittelt …";
            var details = await _controller.GetMediaDetailsAsync(item.Path, token); token.ThrowIfCancellationRequested();
            if (IsDisposed || _exitInProgress) return;
            using var dialog = new ClipDialog(item.FileName, details);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            destination = ChooseExportDestination(item, true); if (destination is null) return; token.ThrowIfCancellationRequested();
            _archiveProgress.Text = "Clip wird kopiert · Aufnahmebedienung bleibt verfügbar.";
            await _controller.ExportClipAsync(item.Path, destination, dialog.SelectedRange, token);
        }, "Clip wird vorbereitet", () => destination is null ? "Clip-Export verworfen." : "Clip exportiert: " + Path.GetFileName(destination));
    }
    private string? ChooseExportDestination(RecordingInfo item, bool clip)
    {
        using var dialog = new SaveFileDialog { Title = clip ? "Clip als MP4 speichern" : "Aufnahme als MP4 exportieren", Filter = "MP4-Video (*.mp4)|*.mp4", DefaultExt = "mp4", AddExtension = true, FileName = Path.GetFileNameWithoutExtension(item.Path) + (clip ? "_Clip" : "") + ".mp4", OverwritePrompt = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        if (File.Exists(dialog.FileName)) { ShowError(new IOException("Diese Datei existiert bereits. Wähle einen neuen Namen; vorhandene Exporte bleiben erhalten.")); return null; }
        return dialog.FileName;
    }
    private async Task RunArchiveExportAsync(Func<CancellationToken, Task> operation, string progress, Func<string> success)
    {
        if (_uiBusy || _exporting || _exitInProgress) return;
        _exportCancellation = new CancellationTokenSource(); _exporting = true; _exportProgress.Visible = _cancelExport.Visible = true;
        _archiveProgressLayout.ColumnStyles[1].Width = 95; _archiveProgressLayout.ColumnStyles[2].Width = 96;
        _archiveProgress.Text = progress + " · Aufnahmebedienung bleibt verfügbar."; UpdateRecordingActions();
        try { _activeExportTask = operation(_exportCancellation.Token); await _activeExportTask; if (!IsDisposed) _footer.Text = success(); }
        catch (OperationCanceledException) { if (!IsDisposed) _footer.Text = "Export abgebrochen. Die Originalaufnahme bleibt erhalten."; }
        catch (Exception ex) { if (!IsDisposed && !_exitInProgress) ShowError(ex); }
        finally
        {
            _exporting = false; _activeExportTask = null; _exportCancellation.Dispose(); _exportCancellation = null;
            if (!IsDisposed) { _exportProgress.Visible = _cancelExport.Visible = false; _archiveProgressLayout.ColumnStyles[1].Width = _archiveProgressLayout.ColumnStyles[2].Width = 0; RefreshDashboard(); }
        }
    }
    private void CancelExport() { _exportCancellation?.Cancel(); _archiveProgress.Text = "Export wird abgebrochen …"; }
    private sealed class ClipDialog : Form
    {
        private readonly NumericUpDown _start = Number(0, 1), _duration = Number(0, 1);
        private readonly Label _rangeSummary = TextLabel("", 10, Cyan);
        private readonly Button _accept = ActionButton("Clip speichern …", true);
        private readonly decimal _totalSeconds;
        public ClipRange SelectedRange => new(TimeSpan.FromSeconds((double)_start.Value), TimeSpan.FromSeconds((double)_duration.Value));

        public ClipDialog(string fileName, MediaDetails details)
        {
            _totalSeconds = decimal.Floor((decimal)details.Duration.TotalSeconds * 1000) / 1000;
            if (_totalSeconds < 0.001m) throw new InvalidOperationException("Die Aufnahme hat keine auswertbare Videodauer.");
            Text = "Moment als Clip speichern"; ClientSize = new Size(590, 386); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 10); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var layout = Grid(1, 7); layout.Padding = new Padding(23); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 87)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(TextLabel(fileName, 13, Ink, FontStyle.Bold), 0, 0);
            layout.Controls.Add(TextLabel($"Gesamtdauer {FormatDuration(details.Duration)}   ·   {details.Width} × {details.Height}   ·   {(details.HasAudio ? "Mit Ton" : "Ohne Tonspur")}", 9, Muted), 0, 1);
            var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0) };
            foreach (var preset in new[] { (30, "Letzte 30 Sek."), (60, "Letzte Minute"), (300, "Letzte 5 Min.") })
            {
                int seconds = preset.Item1; var button = ActionButton(preset.Item2); button.Name = "Last" + seconds; button.Size = new Size(151, 34); button.Font = new Font("Segoe UI", 9); button.Click += (_, _) => SelectLast(seconds); quick.Controls.Add(button);
            }
            layout.Controls.Add(quick, 0, 2);
            var fields = Grid(2, 2); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            _start.Name = "ClipStart"; _duration.Name = "ClipDuration"; _start.DecimalPlaces = _duration.DecimalPlaces = 3; _start.Maximum = _totalSeconds - 0.001m; _duration.Minimum = 0.001m; _duration.Maximum = _totalSeconds; _start.Width = _duration.Width = 110;
            fields.Controls.Add(TextLabel("Startposition", 9.5f, Muted), 0, 0); fields.Controls.Add(WithUnit(_start, "Sekunden ab Aufnahmebeginn"), 1, 0); fields.Controls.Add(TextLabel("Cliplänge", 9.5f, Muted), 0, 1); fields.Controls.Add(WithUnit(_duration, "Sekunden ab Startposition"), 1, 1); layout.Controls.Add(fields, 0, 3);
            layout.Controls.Add(_rangeSummary, 0, 4); var hint = TextLabel("Schneller Export ohne erneute Kodierung. Der Start kann wegen\nder Schlüsselbilder etwas früher liegen. Die Originalaufnahme bleibt erhalten.", 9, Muted); hint.TextAlign = ContentAlignment.TopLeft; hint.Padding = new Padding(0, 5, 0, 0); layout.Controls.Add(hint, 0, 5);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty }; _accept.Size = new Size(168, 39); _accept.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.Size = new Size(118, 39); cancel.DialogResult = DialogResult.Cancel; actions.Controls.AddRange([_accept, cancel]); layout.Controls.Add(actions, 0, 6); Controls.Add(layout); AcceptButton = _accept; CancelButton = cancel;
            _start.ValueChanged += (_, _) => UpdateRange(); _duration.ValueChanged += (_, _) => UpdateRange(); SelectLast(30);
        }
        private void SelectLast(int seconds) { decimal duration = Math.Min(_totalSeconds, seconds); _start.Value = _totalSeconds - duration; _duration.Value = duration; UpdateRange(); }
        private void UpdateRange()
        {
            bool valid = _duration.Value > 0 && _start.Value + _duration.Value <= _totalSeconds; _accept.Enabled = valid;
            _rangeSummary.Text = valid ? $"{FormatDuration(TimeSpan.FromSeconds((double)_start.Value))} → {FormatDuration(TimeSpan.FromSeconds((double)(_start.Value + _duration.Value)))}   ·   {_duration.Value:0.###} Sek. Clip" : "Der Clip reicht über das Ende. Bitte die Cliplänge verkürzen."; _rangeSummary.ForeColor = valid ? Cyan : Amber;
        }
    }
    private async Task TestAudioAsync()
    {
        if (_audio.SelectedItem is not AudioSource source) return;
        await RunActionAsync(async () =>
        {
            using var capture = await Task.Run(() => new AudioLoopback(source.Id));
            capture.Start(); double peak = 0;
            try
            {
                for (int i = 0; i < 40 && !_exitInProgress && !IsDisposed; i++)
                {
                    await Task.Delay(125); peak = Math.Max(peak, capture.PeakLevel);
                    _audioTestResult.Text = $"Prüfe {capture.DeviceName} · Pegel {peak * 100:0} %";
                }
                if (!IsDisposed) { _audioTestResult.Text = peak > 0.0001 ? $"✓ Ton empfangen · {capture.DeviceName}" : "Kein Ton empfangen. Spiel starten oder die Spiel-/Headset-Ausgabe wählen."; _audioTestResult.ForeColor = peak > 0.0001 ? Cyan : Amber; }
            }
            finally { await capture.StopAsync(); }
        });
    }
    private Task SaveSettingsAsync() => RunActionAsync(async () => { await _controller.SaveSettingsAsync(ReadSettings()); LoadSettings(); _footer.Text = "Einstellungen gespeichert."; });
    private void TrackSettingsChanges()
    {
        foreach (var control in Descendants(_settingsFields))
        {
            if (control is TextBox entry) entry.TextChanged += (_, _) => UpdateDirtyState();
            if (control is ComboBox choice) choice.SelectedIndexChanged += (_, _) => UpdateDirtyState();
            if (control is CheckBox check) check.CheckedChanged += (_, _) => UpdateDirtyState();
            if (control is NumericUpDown number) number.ValueChanged += (_, _) => UpdateDirtyState();
        }
    }
    private static IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private string SettingsFingerprint() => string.Join("\u001f", Descendants(_settingsFields).Where(c => c is TextBox or ComboBox or CheckBox or NumericUpDown).Select(c => c switch { CheckBox check => check.Checked.ToString(), NumericUpDown number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ComboBox choice => choice.SelectedItem is DisplaySource display ? display.Id : choice.SelectedItem is AudioSource audio ? audio.Id : choice.SelectedIndex.ToString(), _ => c.Text }));
    private void UpdateDirtyState() { if (_loadingSettings) return; _settingsDirty = SettingsFingerprint() != _settingsBaseline; RefreshDashboard(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S) && _page == 1 && _save.Enabled) { _ = SaveSettingsAsync(); return true; }
        if (keyData == (Keys.Control | Keys.Alt | Keys.F11) && _bookmark.Enabled) { _ = AddBookmarkAsync(); return true; }
        if (_page == 2)
        {
            if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); _search.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.F)) { ResetArchiveFilters(); return true; }
            if (keyData == Keys.F5) { RefreshRecordings(); return true; }
            if (_recordings.ContainsFocus)
            {
                if (keyData == Keys.Enter && _play.Enabled) { PlaySelected(); return true; }
                if (keyData == Keys.F2 && _labelSelected.Enabled) { _ = LabelSelectedAsync(); return true; }
                if (keyData == Keys.Delete && _delete.Enabled) { _ = DeleteSelectedAsync(); return true; }
                if (keyData == (Keys.Control | Keys.E) && _export.Enabled) { _ = ExportSelectedAsync(); return true; }
                if (keyData == (Keys.Control | Keys.Shift | Keys.E) && _clip.Enabled) { _ = ExportClipSelectedAsync(); return true; }
                if (keyData == (Keys.Control | Keys.G) && _protectSelected.Enabled) { _ = ProtectSelectedAsync(true); return true; }
                if (keyData == (Keys.Control | Keys.Shift | Keys.G) && _releaseSelected.Enabled) { _ = ProtectSelectedAsync(false); return true; }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private async Task ToggleRecordingActionAsync()
    {
        if (_stopping || _exitInProgress) return;
        var status = _controller.Status;
        if (status.Armed || status.Recording || status.Paused) await RequestStopAsync();
        else if (!_uiBusy && !status.Busy) await RunActionAsync(_controller.ToggleRecordingAsync);
    }
    private async Task RequestStopAsync()
    {
        if (_stopping || _exitInProgress) return;
        _stopping = true; RefreshDashboard();
        try { await _controller.StopAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && !_exitInProgress) ShowError(ex); }
        finally { _stopping = false; if (!IsDisposed) { RefreshDashboard(); if (_page == 2) RefreshRecordings(); } }
    }
    private async Task RunActionAsync(Func<Task> action) { if (_uiBusy || _stopping || _exitInProgress) return; _uiBusy = true; RefreshDashboard(); try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex); } finally { _uiBusy = false; if (!IsDisposed) { RefreshDashboard(); if (_page == 2) RefreshRecordings(); } } }
    private void RunSync(Action action) { try { action(); } catch (Exception ex) { ShowError(ex); } }
    private void ShowError(Exception error) { string message = error.Message.Split(" Details:", StringSplitOptions.None)[0]; if (message.Length > 600) message = message[..600] + " … Weitere Informationen in der Diagnose."; _footer.Text = message; if (Visible && WindowState != FormWindowState.Minimized) MessageBox.Show(this, message, "ScreenDash · Aktion nicht möglich", MessageBoxButtons.OK, MessageBoxIcon.Warning); else Notify(message, ToolTipIcon.Warning); }
    private void FitWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        if (Width > area.Width || Height > area.Height) Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        // Offscreen preview harnesses deliberately use negative coordinates.
        if (Left > -10000 && Top > -10000) Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }
    private void MinimizeToTaskbar() { if (IsDisposed || Disposing) return; ShowInTaskbar = true; WindowState = FormWindowState.Minimized; }
    private void ShowWindow() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); RefreshDashboard(); if (_page == 2) RefreshRecordings(); }
    private async void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose) return; bool windowsClosing = e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing;
        if (!_exitRequested && !windowsClosing && (_controller.Status.Armed || _controller.Status.Recording || _controller.Status.Paused || _controller.Status.Busy || _uiBusy || _exporting)) { e.Cancel = true; MinimizeToTaskbar(); if (!_hiddenNoticeShown) { _hiddenNoticeShown = true; Notify("ScreenDash läuft weiter und bleibt in der Taskleiste erreichbar. Zum Beenden das Menü im Infobereich verwenden."); } return; }
        e.Cancel = true; await ExitAsync();
    }
    private async Task ExitAsync() { if (_exitInProgress || _allowClose) return; _exitRequested = true; _exitInProgress = true; RefreshDashboard(); try { _updateCancellation?.Cancel(); if (_activeUpdateTask is { } updateTask) { try { await updateTask; } catch { } } _exportCancellation?.Cancel(); if (_activeExportTask is { } export) { try { await export; } catch (OperationCanceledException) { } catch { } } await _controller.StopAsync(); _allowClose = true; _tray.Visible = false; Close(); } catch (Exception ex) { _exitRequested = false; _exitInProgress = false; ShowWindow(); ShowError(ex); } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyTitleBar(this); if (!_enableHotkeys) return; const uint modifiers = 0x0001 | 0x0002 | 0x4000; _hotkeyToggle = RegisterHotKey(Handle, 8, modifiers, (uint)Keys.F8); _hotkeyProtect = RegisterHotKey(Handle, 9, modifiers, (uint)Keys.F9); _hotkeyPause = RegisterHotKey(Handle, 10, modifiers, (uint)Keys.F10); _hotkeyBookmark = RegisterHotKey(Handle, 11, modifiers, (uint)Keys.F11); if (!_hotkeyToggle || !_hotkeyProtect || !_hotkeyPause || !_hotkeyBookmark) _footer.Text = "Ein Tastenkürzel ist bereits belegt. Nutze die Schaltflächen oder das Menü im Infobereich."; }
    protected override void OnHandleDestroyed(EventArgs e) { if (_hotkeyToggle) UnregisterHotKey(Handle, 8); if (_hotkeyProtect) UnregisterHotKey(Handle, 9); if (_hotkeyPause) UnregisterHotKey(Handle, 10); if (_hotkeyBookmark) UnregisterHotKey(Handle, 11); _hotkeyToggle = _hotkeyProtect = _hotkeyPause = _hotkeyBookmark = false; base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message m) { if (m.Msg == 0x0312) { int id = m.WParam.ToInt32(); if (id == 8) { _ = ToggleRecordingActionAsync(); return; } if (id == 9) { _ = RunActionAsync(_controller.ProtectRecentAsync); return; } if (id == 11 && _bookmark.Enabled) { _ = AddBookmarkAsync(); return; } if (id == 10 && (_controller.Status.Armed || _controller.Status.Recording)) { _ = RunActionAsync(TogglePauseAsync); return; } } base.WndProc(ref m); }
    protected override void Dispose(bool disposing) { if (disposing) { DisposeMotion(); _controller.Changed -= ControllerChanged; _controller.Notice -= ControllerNotice; _archiveReloadCancellation.Cancel(); _exportCancellation?.Cancel(); _updateCancellation?.Cancel(); _archiveMenu.Dispose(); _tray.Visible = false; _tray.Dispose(); _trayMenu.Dispose(); _tips.Dispose(); } base.Dispose(disposing); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static TableLayoutPanel Grid(int columns, int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, BackColor = Color.Transparent };
        if (columns == 1) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }
    private static Label TextLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new() { Text = text, Dock = DockStyle.Fill, AutoSize = false, ForeColor = color, Font = new Font("Segoe UI", size, style), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, BackColor = Color.Transparent, AutoEllipsis = true };
    private static Button ActionButton(string text, bool primary = false) { var button = new ThemeButton { Text = text, Primary = primary, FlatStyle = FlatStyle.Flat, BackColor = primary ? Cyan : Input, ForeColor = primary ? Color.FromArgb(18, 20, 24) : Ink, Font = new Font("Segoe UI", 10, primary ? FontStyle.Bold : FontStyle.Regular), UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 9, 0), TabStop = true }; button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(233, 237, 242) : Color.FromArgb(47, 51, 57); button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(187, 194, 203) : Color.FromArgb(61, 66, 74); return button; }
    private static Panel Surface(Padding padding) => new RoundedPanel { Dock = DockStyle.Fill, BackColor = Card, Padding = padding, Margin = new Padding(0, 0, 0, 12) };
    private static Panel Metric(string heading, Label value, string detail, bool last) => Metric(heading, value, TextLabel(detail, 8, Muted), last);
    private static Panel Metric(string heading, Label value, Label detail, bool last) { var panel = Surface(new Padding(16, 12, 16, 12)); panel.Margin = new Padding(0, 0, last ? 0 : 10, 12); var grid = Grid(1, 3); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); grid.Controls.Add(TextLabel(heading, 7.8f, Cyan, FontStyle.Bold), 0, 0); grid.Controls.Add(value, 0, 1); grid.Controls.Add(detail, 0, 2); panel.Controls.Add(grid); return panel; }
    private static TableLayoutPanel SettingsGrid(Panel parent) { var fields = Grid(2, 0); fields.Dock = DockStyle.Top; fields.AutoSize = true; fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 187)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); parent.Controls.Add(fields); return fields; }
    private static void AddField(TableLayoutPanel fields, string title, Control control, int height = 43)
    {
        int row = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); fields.Controls.Add(TextLabel(title, 9.5f, Muted), 0, row); control.Margin = new Padding(0, 5, 0, 5); fields.Controls.Add(control, 1, row);
        if (string.IsNullOrWhiteSpace(title)) return;
        foreach (var field in Descendants(control).Prepend(control))
            if (field is TextBox or ComboBox or NumericUpDown or DateTimePicker && string.IsNullOrWhiteSpace(field.AccessibleName)) field.AccessibleName = title;
    }
    private static void AddOption(TableLayoutPanel fields, CheckBox control, int height = 35) { int row = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); control.Margin = Padding.Empty; fields.Controls.Add(control, 1, row); }
    private static void AddWide(TableLayoutPanel fields, Control control, int height) { int row = fields.RowCount++; fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); fields.Controls.Add(control, 0, row); fields.SetColumnSpan(control, 2); }
    private static void AddHeading(TableLayoutPanel fields, string title, string help) { var titleLabel = TextLabel(title, 18, Ink); titleLabel.Font = new Font("Georgia", 18, FontStyle.Italic); AddWide(fields, titleLabel, 36); var label = TextLabel(help, 9, Muted); label.TextAlign = ContentAlignment.TopLeft; label.Padding = new Padding(0, 5, 0, 0); AddWide(fields, label, 42); }
    private static void AddHelp(TableLayoutPanel fields, string text) { var label = TextLabel(text, 9, Muted); label.Padding = new Padding(0, 17, 0, 0); label.TextAlign = ContentAlignment.TopLeft; AddWide(fields, label, 72); }
    private static TextBox Entry() => new ThemeTextBox { Dock = DockStyle.Fill, BackColor = Input, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
    private static ComboBox Choice() { var choice = new ThemeComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Input, ForeColor = Ink, IntegralHeight = true, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 25 }; choice.DrawItem += (_, e) => { bool highlight = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0; using var background = new SolidBrush(highlight ? Color.FromArgb(69, 76, 85) : Input); e.Graphics.FillRectangle(background, e.Bounds); string text = e.Index >= 0 ? (choice.GetItemText(choice.Items[e.Index]) ?? "") : choice.Text; TextRenderer.DrawText(e.Graphics, text, e.Font, new Rectangle(e.Bounds.X + 7, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 9), e.Bounds.Height), choice.Enabled ? Ink : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix); e.DrawFocusRectangle(); }; return choice; }
    private static CheckBox Check(string text) => new ThemeCheckBox { Text = text, AccessibleName = text, Dock = DockStyle.Fill, ForeColor = Ink, AutoSize = false, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f) };
    private static NumericUpDown Number(int minimum, int maximum) => new() { Minimum = minimum, Maximum = maximum, Width = 98, BackColor = Input, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right, ThousandsSeparator = true, Font = new Font("Segoe UI", 10) };
    private static Control WithUnit(NumericUpDown number, string unit) { var panel = Grid(2, 1); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); number.Anchor = AnchorStyles.Left; number.Margin = new Padding(0, 3, 10, 3); number.AccessibleDescription = unit; panel.Controls.Add(number, 0, 0); panel.Controls.Add(TextLabel(unit, 9, Muted), 1, 0); return panel; }
    private static void SetNumber(NumericUpDown control, int value) => control.Value = Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.0} GB" : $"{bytes / (1024d * 1024):0.0} MB";
    private static string FormatDuration(TimeSpan value) { value = value < TimeSpan.Zero ? TimeSpan.Zero : value; return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"; }
    private static ContextMenuStrip DarkMenu() => new() { BackColor = Card, ForeColor = Ink, Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
    private static GraphicsPath RoundPath(RectangleF rect, float radius) { float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)); var path = new GraphicsPath(); if (d <= 0) { path.AddRectangle(rect); return path; } path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90); path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path; }
    private static Color SurroundingColor(Control control)
    {
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent) if (parent.BackColor.A == 255) return parent.BackColor;
        return Canvas;
    }
    private sealed class ThemeComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0085 && IsHandleCreated)
            {
                IntPtr dc = GetWindowDC(Handle);
                if (dc != IntPtr.Zero)
                {
                    try { using var frame = Graphics.FromHdc(dc); using var pen = new Pen(Focused ? Cyan : Line); frame.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
                    finally { ReleaseDC(Handle, dc); }
                }
            }
            if (m.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated || Width < 25 || Height < 5) return;
            using var graphics = m.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(m.WParam);
            int arrowWidth = 20 * DeviceDpi / 96;
            using var background = new SolidBrush(Input); graphics.FillRectangle(background, Width - arrowWidth, 1, arrowWidth - 1, Height - 2);
            using var border = new Pen(Focused ? Cyan : Line); graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            if (m.Msg is 0x0317 or 0x0318) graphics.DrawRectangle(border, -1, -1, Width, Height);
            int middle = Width - arrowWidth / 2, y = Height / 2;
            using var arrow = new Pen(Enabled ? Muted : Mix(Muted, Input, .4), 1.3f);
            graphics.DrawLines(arrow, new Point[] { new(middle - 4, y - 2), new(middle, y + 2), new(middle + 4, y - 2) });
        }
    }
    private sealed class ThemeCheckBox : CheckBox
    {
        public ThemeCheckBox() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int boxSize = 16 * DeviceDpi / 96; int y = (Height - boxSize) / 2;
            using var shape = RoundPath(new RectangleF(0, y, boxSize, boxSize), 4);
            using var fill = new SolidBrush(Checked ? (Enabled ? Cyan : Mix(Cyan, Input, .6)) : Input); using var border = new Pen(Checked ? Cyan : Line);
            e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
            if (Checked) { using var check = new Pen(Color.FromArgb(18, 20, 24), 2 * DeviceDpi / 96f); e.Graphics.DrawLines(check, new PointF[] { new(boxSize * .23f, y + boxSize * .53f), new(boxSize * .43f, y + boxSize * .73f), new(boxSize * .79f, y + boxSize * .28f) }); }
            var textArea = new Rectangle(boxSize + 10, 0, Math.Max(0, Width - boxSize - 10), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textArea, Enabled ? Ink : Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(textArea, -2, -5), Muted, SurroundingColor(this));
        }
    }
    private sealed class ThemeTextBox : TextBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is not (0x000F or 0x0317 or 0x0318) || !IsHandleCreated || TextLength > 0 || string.IsNullOrWhiteSpace(PlaceholderText)) return;
            using var graphics = m.Msg == 0x000F ? Graphics.FromHwnd(Handle) : Graphics.FromHdc(m.WParam);
            using var background = new SolidBrush(Input); graphics.FillRectangle(background, Rectangle.Inflate(ClientRectangle, -2, -1));
            TextRenderer.DrawText(graphics, PlaceholderText, Font, Rectangle.Inflate(ClientRectangle, -4, 0), Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    private sealed class StorageMeter : Control
    {
        private double _fraction, _protectedFraction; private bool _warning;
        public double Fraction { get => _fraction; set { if (_fraction != value) { _fraction = value; Invalidate(); } } } public double ProtectedFraction { get => _protectedFraction; set { if (_protectedFraction != value) { _protectedFraction = value; Invalidate(); } } } public bool Warning { get => _warning; set { if (_warning != value) { _warning = value; Invalidate(); } } }
        public StorageMeter() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Margin = new Padding(0, 4, 0, 4); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var path = RoundPath(new RectangleF(0, 0, Width, Height), 3); using var background = new SolidBrush(Line); e.Graphics.FillPath(background, path); e.Graphics.SetClip(path); using var fill = new SolidBrush(_warning ? Amber : Cyan); using var protect = new SolidBrush(Violet); e.Graphics.FillRectangle(fill, 0, 0, (int)(ClientSize.Width * _fraction), ClientSize.Height); e.Graphics.FillRectangle(protect, 0, 0, (int)(ClientSize.Width * Math.Min(_fraction, _protectedFraction)), ClientSize.Height); }
    }
    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Ink : Muted; base.OnRenderItemText(e); }
        private sealed class DarkColors : ProfessionalColorTable { public override Color ToolStripDropDownBackground => Card; public override Color MenuItemSelected => Input; public override Color MenuItemBorder => Line; public override Color MenuBorder => Line; public override Color SeparatorDark => Line; public override Color SeparatorLight => Line; }
    }
}
