namespace ScreenDash;

public sealed partial class MainForm
{
    private readonly Panel _toolsPage = new() { Dock = DockStyle.Fill }, _toolFields = new() { Dock = DockStyle.Fill };
    private readonly Button[] _toolNav = new Button[3];
    private readonly Panel[] _toolTabs = new Panel[3];
    private readonly Button _bookmark = ActionButton("◆ Lesezeichen"), _bookmarksSelected = ActionButton("Lesezeichen …"), _findTime = ActionButton("Zeitpunkt …");
    private readonly ToolStripMenuItem _trayBookmark;
    private readonly CheckBox _microphoneEnabled = Check("Mikrofon zusätzlich aufnehmen · standardmäßig aus"), _warnOnSilence = Check("Bei fehlendem Ton oder getrenntem Gerät warnen"), _gameDetection = Check("Bei ausgewählten Spielen automatisch aufnehmen");
    private readonly ComboBox _microphone = Choice(), _savedProfiles = Choice();
    private readonly NumericUpDown _systemGain = Number(0, 200), _microphoneGain = Number(0, 200), _silenceSeconds = Number(10, 600);
    private readonly TextBox _profileName = Entry(), _gameNames = Entry(), _updateDirectory = Entry();
    private readonly Button _openUpdateFolder = ActionButton("Ordner öffnen"), _testMicrophone = ActionButton("Mikrofon testen");
    private readonly Button _saveProfile = ActionButton("Profil speichern", true), _applyProfile = ActionButton("Anwenden"), _deleteProfile = ActionButton("Löschen"), _pickGames = ActionButton("Laufende Spiele …"), _saveGames = ActionButton("Spielerkennung speichern", true), _checkUpdate = ActionButton("Update prüfen"), _downloadUpdate = ActionButton("Update vorbereiten", true), _launchUpdate = ActionButton("Jetzt wechseln …"), _cancelUpdate = ActionButton("Abbrechen");
    private readonly Label _profilesHint = TextLabel("Bildschirm, Ton und Qualität als eigenes Profil speichern.", 9, Muted), _gamesHint = TextLabel("Spielnamen mit .exe eintragen, durch Komma trennen. Erkennung ist standardmäßig aus.", 9, Muted), _updateHint = TextLabel("Updates werden erst auf deinen Klick bei GitHub geprüft.", 9, Muted);
    private UpdateInfo? _availableUpdate;
    private PreparedUpdate? _preparedUpdate;
    private CancellationTokenSource? _updateCancellation;
    private Task? _activeUpdateTask;
    private bool _checkingUpdate, _profilesLoading, _gamesBusy, _hotkeyBookmark;
    private string _gamesBaseline = "";

    private void BuildAudioFeatures(TableLayoutPanel fields)
    {
        AddField(fields, "Spielton-Lautstärke", WithUnit(_systemGain, "% · 100 entspricht dem Originalpegel"));
        AddOption(fields, _microphoneEnabled, 38); AddField(fields, "Mikrofon", _microphone); AddField(fields, "Stimmen-Lautstärke", WithUnit(_microphoneGain, "% · Spielton und Stimme werden in einer Tonspur gemischt"));
        var access = ActionButton("Mikrofonzugriff"); access.Click += (_, _) => RunSync(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true })); AddField(fields, "", FeatureActions(_testMicrophone, access), 43); _testMicrophone.Click += async (_, _) => await TestMicrophoneAsync();
        AddOption(fields, _warnOnSilence, 38); AddField(fields, "Stille melden nach", WithUnit(_silenceSeconds, "Sekunden · kurze stille Spielmomente bleiben ohne Hinweis"));
        _microphoneEnabled.CheckedChanged += (_, _) => _microphone.Enabled = _microphoneGain.Enabled = _testMicrophone.Enabled = _microphoneEnabled.Checked;
        _warnOnSilence.CheckedChanged += (_, _) => _silenceSeconds.Enabled = _warnOnSilence.Checked;
        _tips.SetToolTip(_microphoneEnabled, "Deine Stimme wird nur bei eingeschalteter Option aufgenommen. Windows muss Mikrofonzugriff für Desktop-Apps erlauben.");
    }

    private void BuildTools()
    {
        var grid = Grid(1, 2); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); _toolsPage.Controls.Add(grid);
        var tabs = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty }; string[] titles = ["Aufnahmeprofile", "Spielerkennung", "Updates"];
        for (int i = 0; i < titles.Length; i++) { int tab = i; _toolNav[i] = ActionButton(titles[i]); _toolNav[i].Size = new Size(164, 36); _toolNav[i].Click += (_, _) => ShowToolTab(tab); tabs.Controls.Add(_toolNav[i]); }
        grid.Controls.Add(tabs, 0, 0); grid.Controls.Add(_toolFields, 0, 1);
        var profiles = Surface(new Padding(22, 15, 22, 15)); var pf = SettingsGrid(profiles); AddHeading(pf, "Deine Aufnahmeprofile", "Quellen, Mikrofon und Qualität mit einem Klick wiederverwenden.");
        _savedProfiles.DisplayMember = nameof(RecordingProfile.Name); AddField(pf, "Gespeichertes Profil", _savedProfiles); _profileName.PlaceholderText = "z. B. Shooter · Headset"; _profileName.MaxLength = 60; AddField(pf, "Neues Profil", _profileName);
        var pa = FeatureActions(_saveProfile, _applyProfile, _deleteProfile); AddWide(pf, pa, 44); AddWide(pf, _profilesHint, 55); AddHelp(pf, "Passe zunächst Bildschirm, Spielton, Mikrofon und Qualität unter Einstellungen an.\nDas Profil speichert auch deinen Entwurf. Speichergrenzen und Windows-Autostart werden beim Anwenden nicht geändert."); _toolTabs[0] = profiles; _toolFields.Controls.Add(profiles);
        _saveProfile.Click += async (_, _) => await SaveProfileAsync(); _applyProfile.Click += async (_, _) => await ApplySavedProfileAsync(); _deleteProfile.Click += async (_, _) => await DeleteProfileAsync(); _savedProfiles.SelectedIndexChanged += (_, _) => RefreshFeatureActions(); _profileName.TextChanged += (_, _) => RefreshFeatureActions();
        var games = Surface(new Padding(22, 15, 22, 15)); var gf = SettingsGrid(games); AddHeading(gf, "Spiel gestartet? Rückblick läuft.", "Prüft alle paar Sekunden nur die Prozessnamen. Manuelle Aufnahme bleibt verfügbar."); AddWide(gf, _gameDetection, 35); _gameNames.PlaceholderText = "game.exe, anderes-spiel.exe"; AddField(gf, "Spiele / Programme", _gameNames);
        var ga = FeatureActions(_pickGames, _saveGames); AddWide(gf, ga, 45); AddWide(gf, _gamesHint, 60); AddHelp(gf, "Die Erkennung verwendet deine gespeicherten Aufnahme-Einstellungen.\nNach manuellem Stopp startet sie erst wieder, wenn alle ausgewählten Spiele beendet und neu geöffnet wurden."); _toolTabs[1] = games; _toolFields.Controls.Add(games); _pickGames.Click += async (_, _) => await PickGamesAsync(); _saveGames.Click += async (_, _) => await SaveGamesAsync(); _gameNames.TextChanged += (_, _) => RefreshFeatureActions(); _gameDetection.CheckedChanged += (_, _) => RefreshFeatureActions();
        var updates = Surface(new Padding(22, 15, 22, 15)); var uf = SettingsGrid(updates); AddHeading(uf, "ScreenDash aktuell halten", "Neue portable Windows-Version herunterladen, prüfen und Einstellungen behalten."); AddWide(uf, _updateHint, 80); var ua = FeatureActions(_checkUpdate, _downloadUpdate, _launchUpdate, _cancelUpdate); AddWide(uf, ua, 46);
        _updateDirectory.ReadOnly = true; _updateDirectory.Text = "Nach dem Vorbereiten verfügbar"; var location = Grid(2, 1); location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); location.Controls.Add(_updateDirectory, 0, 0); _openUpdateFolder.Dock = DockStyle.Fill; _openUpdateFolder.Font = new Font("Segoe UI", 9); _openUpdateFolder.Margin = new Padding(8, 0, 0, 0); location.Controls.Add(_openUpdateFolder, 1, 0); AddField(uf, "Neuer Versionsordner", location, 44); _openUpdateFolder.Click += (_, _) => { if (_preparedUpdate is { } update) RunSync(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(update.ExecutablePath) ?? update.Directory) { UseShellExecute = true })); };
        var updateHelp = TextLabel("Beim Wechsel wird die Aufnahme gespeichert. Aufnahmen und Einstellungen bleiben erhalten.\nDie neue portable EXE startet aus einem neuen Versionsordner. Alte Verknüpfungen öffnen weiterhin\ndie alte Version; verwende danach die neue ScreenDash.exe aus dem angezeigten Ordner.", 9, Muted); updateHelp.TextAlign = ContentAlignment.TopLeft; updateHelp.Padding = new Padding(0, 17, 0, 0); AddWide(uf, updateHelp, 98); _toolTabs[2] = updates; _toolFields.Controls.Add(updates);
        _checkUpdate.Click += async (_, _) => await CheckUpdateAsync(); _downloadUpdate.Click += async (_, _) => await PrepareUpdateAsync(); _launchUpdate.Click += async (_, _) => await LaunchUpdateAsync(); _cancelUpdate.Click += (_, _) => _updateCancellation?.Cancel();
        foreach (var panel in _toolTabs) { panel.AutoScroll = true; panel.Margin = Padding.Empty; } ShowToolTab(0);
    }

    private void ShowToolTab(int tab) { for (int i = 0; i < _toolTabs.Length; i++) { _toolTabs[i].Visible = i == tab; SetNavigation(_toolNav[i], i == tab); } }

    private static FlowLayoutPanel FeatureActions(params Button[] buttons)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0) };
        foreach (var button in buttons) { button.Size = new Size(button.Text.Length > 19 ? 217 : button.Text.Length > 14 ? 173 : 132, 35); button.Font = new Font("Segoe UI", 9); row.Controls.Add(button); } return row;
    }

    private void LoadFeatureSettings(AppSettings s)
    {
        _microphoneEnabled.Checked = s.MicrophoneEnabled; SetNumber(_systemGain, s.SystemGainPercent); SetNumber(_microphoneGain, s.MicrophoneGainPercent); _warnOnSilence.Checked = s.WarnOnSilence; SetNumber(_silenceSeconds, s.SilenceWarningSeconds); _microphone.Enabled = _microphoneGain.Enabled = _testMicrophone.Enabled = s.MicrophoneEnabled; _systemGain.Enabled = s.AudioEnabled; _silenceSeconds.Enabled = s.WarnOnSilence;
        _gameDetection.Checked = s.GameDetectionEnabled; _gameNames.Text = string.Join(", ", s.GameExecutables); _gamesBaseline = GamesFingerprint();
    }
    private void LoadMicrophones(bool preserveSelection)
    {
        string id = preserveSelection ? (_microphone.SelectedItem as AudioSource)?.Id ?? _controller.Settings.MicrophoneDeviceId : _controller.Settings.MicrophoneDeviceId;
        _microphone.Items.Clear(); foreach (var source in _controller.Microphones) _microphone.Items.Add(source); _microphone.SelectedIndex = FindSource(_microphone, item => ((AudioSource)item).Id == id);
        if (!string.IsNullOrEmpty(id) && !_controller.Microphones.Any(m => m.Id == id)) { _microphone.Items.Add(new AudioSource(id, "Gespeichertes Mikrofon · Nicht verfügbar")); _microphone.SelectedIndex = _microphone.Items.Count - 1; }
    }
    private void ReadFeatureSettings(AppSettings s)
    {
        if (_microphoneEnabled.Checked && _microphone.SelectedItem is not AudioSource) throw new InvalidOperationException("Bitte ein Mikrofon wählen oder die Mikrofonaufnahme deaktivieren.");
        s.MicrophoneEnabled = _microphoneEnabled.Checked; s.MicrophoneDeviceId = (_microphone.SelectedItem as AudioSource)?.Id ?? ""; s.SystemGainPercent = (int)_systemGain.Value; s.MicrophoneGainPercent = (int)_microphoneGain.Value; s.WarnOnSilence = _warnOnSilence.Checked; s.SilenceWarningSeconds = (int)_silenceSeconds.Value;
    }
    private void RefreshFeatureActions()
    {
        if (IsDisposed) return;
        var status = _controller.Status; bool idle = !status.Armed && !status.Recording && !status.Paused && !status.Busy && !_uiBusy && !_stopping && !_exitInProgress;
        _bookmark.Enabled = status.Recording && !status.Busy && !_uiBusy && !_stopping && !_exitInProgress;
        _trayBookmark.Enabled = _bookmark.Enabled;
        _bookmarksSelected.Enabled = !_uiBusy && !_exporting && !_exitInProgress && SelectedRecording() is not null; _findTime.Enabled = !_uiBusy && !_exporting && !_exitInProgress;
        _saveProfile.Enabled = idle && !_profilesLoading && _profileName.Text.Trim().Length > 0; _applyProfile.Enabled = idle && !_profilesLoading && _savedProfiles.SelectedItem is RecordingProfile; _deleteProfile.Enabled = idle && !_profilesLoading && _savedProfiles.SelectedItem is RecordingProfile;
        _gameDetection.Enabled = _gameNames.Enabled = idle && !_gamesBusy; _pickGames.Enabled = idle && !_gamesBusy; _saveGames.Enabled = idle && !_gamesBusy && GamesFingerprint() != _gamesBaseline;
        _checkUpdate.Enabled = !_checkingUpdate && _activeUpdateTask is null && !_exitInProgress; _downloadUpdate.Enabled = !_checkingUpdate && _activeUpdateTask is null && _availableUpdate is { IsNewer: true } && _preparedUpdate is null && !_exitInProgress; _launchUpdate.Enabled = _openUpdateFolder.Enabled = _preparedUpdate is not null && !_checkingUpdate && _activeUpdateTask is null && !_exitInProgress; _cancelUpdate.Visible = _activeUpdateTask is not null;
        _profilesHint.Text = idle ? "Speichert den aktuellen Entwurf aus Einstellungen; Aufnahmeordner und Speicherregeln bleiben beim Anwenden erhalten." : "Stoppe die Aufnahme, um ein Profil zu speichern oder anzuwenden.";
    }
    private string GamesFingerprint() => _gameDetection.Checked + "\n" + _gameNames.Text.Trim();

    private async Task AddBookmarkAsync()
    {
        if (!_bookmark.Enabled) return;
        await RunActionAsync(async () => { await _controller.AddBookmarkAsync(); _footer.Text = "◆ Moment markiert und ganze Datei gesichert. Unter Aufnahmen → Lesezeichen kannst du ihn benennen und als Clip exportieren."; });
    }
    private async Task ReloadProfilesAsync()
    {
        if (_profilesLoading || IsDisposed || _exitInProgress) return; _profilesLoading = true; RefreshFeatureActions();
        try { var profiles = await _controller.GetProfilesAsync(_archiveReloadCancellation.Token); if (IsDisposed) return; string? selected = (_savedProfiles.SelectedItem as RecordingProfile)?.Id; _savedProfiles.Items.Clear(); foreach (var profile in profiles) _savedProfiles.Items.Add(profile); _savedProfiles.SelectedIndex = FindSource(_savedProfiles, item => ((RecordingProfile)item).Id == selected); }
        catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) _profilesHint.Text = "Profile konnten nicht geladen werden: " + ex.Message; }
        finally { _profilesLoading = false; RefreshFeatureActions(); }
    }
    private async Task SaveProfileAsync()
    {
        if (!_saveProfile.Enabled) return;
        await RunActionAsync(async () => { await _controller.SaveProfileAsync(_profileName.Text.Trim(), ReadSettings()); _profileName.Clear(); await ReloadProfilesAsync(); _footer.Text = "Aufnahmeprofil gespeichert."; });
    }
    private async Task ApplySavedProfileAsync()
    {
        if (!_applyProfile.Enabled || _savedProfiles.SelectedItem is not RecordingProfile profile) return;
        if (_settingsDirty && MessageBox.Show(this, "Das Profil übernimmt deine Aufnahme-Einstellungen. Den ungespeicherten Entwurf verwerfen?", "Profil anwenden", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await RunActionAsync(async () => { await _controller.ApplyProfileAsync(profile.Id); LoadSettings(); _footer.Text = "Profil angewendet: " + profile.Name; });
    }
    private async Task DeleteProfileAsync()
    {
        if (!_deleteProfile.Enabled || _savedProfiles.SelectedItem is not RecordingProfile profile) return;
        if (MessageBox.Show(this, $"Profil „{profile.Name}“ löschen?", "Profil löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        await RunActionAsync(async () => { await _controller.DeleteProfileAsync(profile.Id); await ReloadProfilesAsync(); });
    }
    private async Task PickGamesAsync()
    {
        if (!_pickGames.Enabled) return; _gamesBusy = true; RefreshFeatureActions();
        try
        {
            var games = await _controller.GetRunningGamesAsync(_archiveReloadCancellation.Token); if (IsDisposed) return;
            using var dialog = new GamePickerDialog(games, ParseGameNames(_gameNames.Text)); if (dialog.ShowDialog(this) == DialogResult.OK) _gameNames.Text = string.Join(", ", dialog.SelectedNames);
        }
        catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) ShowError(ex); } finally { _gamesBusy = false; RefreshFeatureActions(); }
    }
    private static string[] ParseGameNames(string text) => text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private async Task SaveGamesAsync()
    {
        if (!_saveGames.Enabled) return;
        await RunActionAsync(async () => { var draft = _controller.Settings.Clone(); draft.GameExecutables = ParseGameNames(_gameNames.Text).ToList(); draft.GameDetectionEnabled = _gameDetection.Checked; await _controller.SaveSettingsAsync(draft); _gamesBaseline = GamesFingerprint(); _footer.Text = draft.GameDetectionEnabled ? "Spielerkennung aktiviert. Die Aufnahme startet, sobald ein ausgewähltes Spiel läuft." : "Spielerkennung gespeichert und ausgeschaltet."; });
    }

    private async Task CheckUpdateAsync()
    {
        if (!_checkUpdate.Enabled) return; _checkingUpdate = true; _availableUpdate = null; _preparedUpdate = null; _updateDirectory.Text = "Nach dem Vorbereiten verfügbar"; _updateHint.Text = "GitHub-Version wird geprüft …"; RefreshFeatureActions();
        try { var update = await _controller.CheckForUpdatesAsync(_archiveReloadCancellation.Token); if (IsDisposed) return; _availableUpdate = update; _updateHint.Text = update.IsNewer ? $"Version {update.Version} verfügbar · {FormatBytes(update.Size)}. Beim Vorbereiten wird die Datei auf deinen PC heruntergeladen und geprüft." : $"Du bist aktuell. GitHub-Version: {update.Version}."; }
        catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) _updateHint.Text = "Update-Prüfung fehlgeschlagen: " + ex.Message; }
        finally { _checkingUpdate = false; RefreshFeatureActions(); }
    }
    private async Task PrepareUpdateAsync()
    {
        if (!_downloadUpdate.Enabled || _availableUpdate is not { } update) return;
        _updateCancellation = CancellationTokenSource.CreateLinkedTokenSource(_archiveReloadCancellation.Token); _updateHint.Text = "Update wird heruntergeladen …";
        var progress = new Progress<double>(value => { if (!IsDisposed && _activeUpdateTask is not null) _updateHint.Text = $"Version {update.Version} wird vorbereitet · {Math.Clamp(value, 0, 1) * 100:0} %"; });
        try { _activeUpdateTask = _controller.PrepareUpdateAsync(update, progress, _updateCancellation.Token); RefreshFeatureActions(); _preparedUpdate = await (Task<PreparedUpdate>)_activeUpdateTask; if (!IsDisposed) { _updateHint.Text = $"Version {_preparedUpdate.Version} ist geprüft und bereit. „Jetzt wechseln“ stoppt die Aufnahme und startet die neue portable EXE."; _updateDirectory.Text = Path.GetDirectoryName(_preparedUpdate.ExecutablePath) ?? _preparedUpdate.Directory; } }
        catch (OperationCanceledException) { if (!IsDisposed) _updateHint.Text = "Download abgebrochen. Deine aktuelle Version bleibt verfügbar."; }
        catch (Exception ex) { if (!IsDisposed) _updateHint.Text = "Update konnte nicht vorbereitet werden: " + ex.Message; }
        finally { _activeUpdateTask = null; _updateCancellation.Dispose(); _updateCancellation = null; RefreshFeatureActions(); }
    }
    private async Task LaunchUpdateAsync()
    {
        if (!_launchUpdate.Enabled || _preparedUpdate is not { } update) return;
        if (MessageBox.Show(this, $"Zu ScreenDash {update.Version} wechseln?\n\nDie laufende Aufnahme wird gespeichert. Ein laufender Export wird abgebrochen. Deine Einstellungen und Aufnahmen bleiben erhalten.\n\nDie neue portable EXE startet aus:\n{Path.GetDirectoryName(update.ExecutablePath) ?? update.Directory}\n\nAlte Verknüpfungen öffnen weiterhin die alte Version.", "Update starten", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _exitRequested = _exitInProgress = true; RefreshDashboard();
        try { _exportCancellation?.Cancel(); if (_activeExportTask is { } export) { try { await export; } catch { } } await _controller.StopAsync(); _controller.LaunchPreparedUpdate(update); _allowClose = true; _tray.Visible = false; Close(); }
        catch (Exception ex) { _exitRequested = _exitInProgress = false; ShowError(ex); RefreshDashboard(); }
    }

    private async Task ShowBookmarksAsync()
    {
        if (!_bookmarksSelected.Enabled || SelectedRecording() is not { } recording) return;
        RecordingBookmark? chosen = null;
        await RunActionAsync(async () => { var bookmarks = await _controller.GetBookmarksAsync(recording.Path, _archiveReloadCancellation.Token); if (IsDisposed) return; using var dialog = new BookmarkDialog(_controller, recording, bookmarks); if (dialog.ShowDialog(this) == DialogResult.OK) chosen = dialog.SelectedBookmark; });
        if (chosen is not null && !recording.Active) await ExportMomentAsync(recording, chosen.Offset, chosen.Label);
    }
    private async Task FindTimeAsync()
    {
        if (!_findTime.Enabled) return;
        using var dialog = new TimeSearchDialog(); if (dialog.ShowDialog(this) != DialogResult.OK) return; RecordingTimeMatch? match = null;
        await RunActionAsync(async () => { _footer.Text = "Aufnahme am gewählten Zeitpunkt wird gesucht …"; match = await _controller.FindByTimeAsync(dialog.SelectedTime, _archiveReloadCancellation.Token); if (match is null) _footer.Text = "Für diesen Zeitpunkt ist keine passende Aufnahme vorhanden."; else { _archiveCache = await _controller.GetRecordingsAsync(_archiveReloadCancellation.Token); SelectArchiveRecording(match.Recording); _footer.Text = $"Moment gefunden: {match.Recording.FileName} bei {FormatDuration(match.Offset)}."; } });
        if (match is not null && !match.Recording.Active) await ExportMomentAsync(match.Recording, match.Offset, dialog.SelectedTime.ToString("dd.MM.yyyy HH:mm:ss"));
        else if (match is not null) _footer.Text = "Moment in der laufenden Aufnahme gefunden. Stoppe oder pausiere vor dem Clip-Export.";
    }
    private void SelectArchiveRecording(RecordingInfo item)
    {
        ResetArchiveFilters(); _archiveSortColumn = "Time"; _archiveSortDescending = true; var sorted = _archiveCache.OrderByDescending(r => r.StartedUtc).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray(); int index = Array.FindIndex(sorted, r => r.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)); _archivePage = Math.Max(0, index) / ArchivePageSize; RefreshRecordings(false);
        _recordings.ClearSelection(); foreach (DataGridViewRow row in _recordings.Rows) if (row.Tag is RecordingInfo r && r.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)) { row.Selected = true; _recordings.CurrentCell = row.Cells[0]; _recordings.FirstDisplayedScrollingRowIndex = row.Index; break; }
    }
    private async Task ExportMomentAsync(RecordingInfo recording, TimeSpan offset, string label)
    {
        string? destination = null;
        await RunArchiveExportAsync(async token => { var details = await _controller.GetMediaDetailsAsync(recording.Path, token); if (IsDisposed || _exitInProgress) return; using var dialog = new MomentClipDialog(label, offset, details.Duration); if (dialog.ShowDialog(this) != DialogResult.OK) return; destination = ChooseExportDestination(recording, true); if (destination is null) return; await _controller.ExportClipAsync(recording.Path, destination, dialog.SelectedRange, token); }, "Moment wird vorbereitet", () => destination is null ? "Clip-Export verworfen." : "Moment exportiert: " + Path.GetFileName(destination));
    }
    private async Task TestMicrophoneAsync()
    {
        if (!_testMicrophone.Enabled || !_microphoneEnabled.Checked || _microphone.SelectedItem is not AudioSource source) return;
        await RunActionAsync(async () => { using var capture = await Task.Run(() => new MicrophoneCapture(source.Id)); capture.Start(); double peak = 0; try { for (int i = 0; i < 40 && !_exitInProgress && !IsDisposed; i++) { await Task.Delay(125); peak = Math.Max(peak, capture.PeakLevel); _audioTestResult.Text = $"Mikrofonprüfung · {capture.DeviceName} · {peak * 100:0} %"; } if (!IsDisposed) { _audioTestResult.Text = peak > 0.0001 ? $"✓ Stimme empfangen · {capture.DeviceName}" : "Kein Mikrofonton. Sprich ins Mikrofon und prüfe den Windows-Mikrofonzugriff."; _audioTestResult.ForeColor = peak > 0.0001 ? Cyan : Amber; } } finally { await capture.StopAsync(); } });
    }
}
