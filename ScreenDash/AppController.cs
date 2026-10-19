using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace ScreenDash;

public sealed partial class AppController : IAppController
{
    private readonly Recorder _recorder = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _storageOperations = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _ticker;
    private Task? _backgroundCleanup;
    private readonly RecordingSessionState _session = new();
    private readonly RecordingClock _recordingClock = new();
    private readonly object _startupGate = new();
    private CancellationTokenSource? _pendingStart;
    private long _stopRequests;
    private long _storageRevision;
    private RecordingStore _store;
    private AppSettings _settings;
    private IReadOnlyList<DisplaySource> _displays;
    private IReadOnlyList<AudioSource> _audioDevices;
    private RecorderStatus _recordStatus = new(false, "–", "Bereit", 0, 0, null);
    private CleanupResult? _storage;
    private string _message = "Bereit für die erste Aufnahme";
    private bool _busy, _locked, _suspended, _disposed, _initializing = true;
    private long _recoveringGeneration = -1;
    private double _cpu;
    private long _memory;
    private TimeSpan _previousSelfCpu, _previousChildCpu;
    private int? _previousChildId;
    private long _previousCounter = Stopwatch.GetTimestamp();
    private string? _sessionPrefix;

    public AppSettings Settings => _settings.Clone();
    public IReadOnlyList<DisplaySource> Displays => _displays;
    public IReadOnlyList<AudioSource> AudioDevices => _audioDevices;
    public DashboardStatus Status
    {
        get
        {
            bool armed = _session.Armed;
            var elapsed = armed ? _recordingClock.Elapsed : TimeSpan.Zero;
            return new(_recordStatus.Recording, _busy || (_initializing && !armed), _message, _recordStatus.Encoder, elapsed,
                _cpu, _memory, _recordStatus.Fps, _storage, armed, _session.Paused, _session.PauseReason, _recordStatus.AudioPeak, _recordStatus.AudioDevice,
                RecordingTimer.Remaining(_settings.AutoStopMinutes, elapsed, armed), _recordStatus.MicrophonePeak, _recordStatus.MicrophoneDevice,
                string.IsNullOrEmpty(_recordStatus.AudioWarning) ? _silenceWarning : _recordStatus.AudioWarning,
                _settings.GameDetectionEnabled ? _gameDetectionMessage : "");
        }
    }
    public event Action? Changed;
    public event Action<string>? Notice;

    public AppController(AppSettings? initialSettings = null, ProfileLibrary? profiles = null, GameDetection? games = null, UpdateService? updates = null)
    {
        _profileLibrary = profiles ?? new ProfileLibrary();
        _gameDetection = games ?? new GameDetection();
        _updates = updates ?? new UpdateService();
        _settings = initialSettings is null ? SettingsStore.Load() : SettingsStore.Validate(initialSettings);
        try { _store = new RecordingStore(_settings.RecordingDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Write(ex);
            _settings.RecordingDirectory = new AppSettings().RecordingDirectory;
            _settings.RecordOnLaunch = false;
            _store = new RecordingStore(_settings.RecordingDirectory);
            _message = "Der gespeicherte Aufnahmeordner ist nicht verfügbar. Standardordner gewählt; bitte Einstellungen prüfen und speichern.";
        }
        try { _displays = DisplayCatalog.GetDisplays(); }
        catch (Exception ex) { AppLog.Write(ex); _displays = []; _message = "Bildschirme konnten nicht erkannt werden. Bitte Geräte in den Einstellungen aktualisieren."; }
        try { _audioDevices = AudioLoopback.GetDevices(); }
        catch (Exception ex) { AppLog.Write(ex); _audioDevices = []; }
        try { _microphones = MicrophoneCapture.GetDevices(); }
        catch (Exception ex) { AppLog.Write(ex); _microphones = []; }
        _session.SetPrivacy(false, false, _settings.PauseOnLock);
        if (_message == "Bereit für die erste Aufnahme") _message = "Speicher wird im Hintergrund geprüft …";
        _recorder.Changed += OnRecorderChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ticker = Task.Run(TickAsync);
    }

    public async Task ToggleRecordingAsync()
    {
        if (_session.Armed || _recordStatus.Recording) { await StopAsync(); return; }
        long stopRequest = Interlocked.Read(ref _stopRequests);
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            if (stopRequest != Interlocked.Read(ref _stopRequests)) return;
            if (_session.Armed || _recordStatus.Recording)
            {
                SuppressGameRestart();
                _session.Stop();
                await StopCoreAsync("Aufnahme gestoppt");
            }
            else
            {
                _session.Start();
                _recordingClock.Reset();
                try { await StartCoreAsync(); }
                catch (OperationCanceledException) when (!_disposed) { HandleCanceledStart(); }
                catch { _session.Stop(); throw; }
            }
        }
        finally { _gate.Release(); Publish(); }
    }

    public async Task StopAsync()
    {
        Interlocked.Increment(ref _stopRequests);
        SuppressGameRestart();
        _session.Stop(); CancelPendingStart();
        await _gate.WaitAsync(_lifetime.Token);
        try { _session.Stop(); await StopCoreAsync("Aufnahme gestoppt"); }
        finally { _gate.Release(); Publish(); }
    }

    public async Task PauseAsync()
    {
        _session.Pause(); CancelPendingStart();
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            if (!_session.Armed || !_session.ManualPaused) return;
            await StopCoreAsync("Pausiert · " + _session.PauseReason);
        }
        finally { _gate.Release(); Publish(); }
    }

    public async Task ResumeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_session.Paused) return;
            _session.Resume();
            if (_session.CanCapture)
            {
                try { await StartCoreAsync(); }
                catch (GameStartNoLongerEligibleException) { }
                catch (OperationCanceledException) when (!_disposed) { HandleCanceledStart(); }
                catch { if (_session.Armed) _session.Pause(); throw; }
            }
            else _message = "Pausiert · " + _session.PauseReason;
        }
        finally { _gate.Release(); Publish(); }
    }

    private async Task StartCoreAsync(Func<CancellationToken, Task>? beforeCapture = null)
    {
        beforeCapture ??= PendingGameStartValidation();
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        lock (_startupGate) _pendingStart = startup;
        _busy = true; _message = "Grafik-Encoder wird geprüft …"; Publish();
        bool startupCompleted = false;
        try
        {
            _settings = SettingsStore.Validate(_settings);
            _displays = DisplayCatalog.GetDisplays();
            var display = string.IsNullOrEmpty(_settings.DisplayId) ? _displays.FirstOrDefault() : _displays.FirstOrDefault(d => d.Id == _settings.DisplayId);
            if (display is null) throw new InvalidOperationException("Der gewählte Bildschirm ist nicht verbunden. Wähle in den Einstellungen einen verfügbaren Bildschirm.");
            await _storageOperations.WaitAsync(startup.Token);
            try { SetStorage(await Task.Run(() => _store.Cleanup(_settings, videoPeakBitrateKbps: StorageVideoPeak(_settings)), startup.Token)); }
            finally { _storageOperations.Release(); }
            startup.Token.ThrowIfCancellationRequested();
            if (!_session.CanCapture) throw new OperationCanceledException("Aufnahmestart unterbrochen.", startup.Token);
            if (!_storage.CanRecord) throw new InvalidOperationException(_storage.Message);
            _sessionPrefix = "screendash_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'") + "_" + Guid.NewGuid().ToString("N")[..8];
            await _recorder.StartAsync(_settings.Clone(), display, _sessionPrefix, startup.Token, beforeCapture);
            startup.Token.ThrowIfCancellationRequested();
            if (!_session.CanCapture) throw new OperationCanceledException("Aufnahmestart unterbrochen.", startup.Token);
            if (!_recorder.Status.Recording) throw new IOException("Die Aufnahme wurde bereits beim Start unterbrochen. Prüfe das Protokoll und die Geräte.");
            startupCompleted = true;
            _message = "Aufnahme läuft · " + display.Name;
            AppLog.Write("Aufnahme gestartet: " + _sessionPrefix + " / " + _recorder.Status.Encoder);
        }
        catch (Exception ex)
        {
            _message = FriendlyMessage(ex.Message); AppLog.Write(ex);
            await _recorder.StopAsync(); _sessionPrefix = null;
            throw;
        }
        finally
        {
            lock (_startupGate) { if (ReferenceEquals(_pendingStart, startup)) _pendingStart = null; }
            _busy = false;
            if (startupCompleted && !_recordStatus.Recording && _session.CanCapture && !_disposed) _ = RecoverAsync(_session.Generation);
            Publish();
        }
    }

    private async Task StopCoreAsync(string message)
    {
        _busy = true; Publish();
        try { await _recorder.StopAsync(); _sessionPrefix = null; _recordingClock.End(); _message = message; }
        finally { _busy = false; Publish(); }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var validated = SettingsStore.Validate(settings);
        await _gate.WaitAsync();
        try
        {
            if (_session.Armed || _recordStatus.Recording || _busy) throw new InvalidOperationException("Stoppe die Aufnahme, bevor du Einstellungen änderst.");
            await _storageOperations.WaitAsync(_lifetime.Token);
            try
            {
            // Validate folder access before touching settings/startup; commit the live pair together.
            var candidateStore = await Task.Run(() => new RecordingStore(validated.RecordingDirectory));
            using var startupKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            var previousStartup = startupKey.GetValue("ScreenDash", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var previousKind = previousStartup is null ? RegistryValueKind.String : startupKey.GetValueKind("ScreenDash");
            try
            {
                SetWindowsStartup(validated.StartWithWindows);
                await Task.Run(() => SettingsStore.Save(validated));
            }
            catch
            {
                try
                {
                    if (previousStartup is null) startupKey.DeleteValue("ScreenDash", false);
                    else startupKey.SetValue("ScreenDash", previousStartup, previousKind);
                }
                catch (Exception rollbackError) { AppLog.Write(rollbackError); }
                throw;
            }
            _store = candidateStore;
            bool gamesChanged = _settings.GameDetectionEnabled != validated.GameDetectionEnabled || !_settings.GameExecutables.SequenceEqual(validated.GameExecutables);
            _settings = validated;
            if (gamesChanged) lock (_gameGate) { _gameDetection.Reset(); _gameRecordingOwned = false; _gameAwaitingFirstCapture = false; _gameExitCounter = null; }
            _session.SetPrivacy(_locked, _suspended, validated.PauseOnLock);
            SetStorage(await Task.Run(() => _store.Cleanup(validated, videoPeakBitrateKbps: StorageVideoPeak(validated))));
            _message = "Einstellungen gespeichert";
            }
            finally { _storageOperations.Release(); }
        }
        finally { _gate.Release(); Publish(); }
    }

    private static void SetWindowsStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled) key.SetValue("ScreenDash", "\"" + Application.ExecutablePath + "\" --tray", RegistryValueKind.String);
        else key.DeleteValue("ScreenDash", false);
    }

    public async Task RefreshDevicesAsync()
    {
        var displays = await Task.Run(DisplayCatalog.GetDisplays);
        var audio = await Task.Run(AudioLoopback.GetDevices);
        var microphones = await Task.Run(MicrophoneCapture.GetDevices);
        _displays = displays; _audioDevices = audio; _microphones = microphones; Publish();
    }

    public async Task ProtectRecentAsync()
    {
        await _gate.WaitAsync();
        try
        {
            int count = await Task.Run(() => _store.ProtectRecent(_settings.ProtectMinutes, _sessionPrefix));
            SetStorage(await Task.Run(() => _store.Cleanup(_settings, _sessionPrefix, videoPeakBitrateKbps: StorageVideoPeak(_settings))));
            _message = count > 0 ? $"{count} Videodatei(en) vor automatischem Löschen geschützt" : "Keine Aufnahmen im gewählten Zeitraum vorhanden";
            ShowNotice(_message);
        }
        finally { _gate.Release(); Publish(); }
    }

    public IReadOnlyList<RecordingInfo> GetRecordings() => _store.List(_sessionPrefix);
    public async Task<IReadOnlyList<RecordingInfo>> GetRecordingsAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        // Read snapshots need no capture gate. A slow archive must not block Stop/Pause/privacy.
        var store = _store; string? prefix = _sessionPrefix;
        return await Task.Run(() => store.List(prefix, linked.Token), linked.Token);
    }
    public async Task SetProtectedAsync(string path, bool protect)
        => await SetProtectedManyAsync([path], protect);
    public async Task SetProtectedManyAsync(IReadOnlyList<string> paths, bool protect)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() => { foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) _store.SetProtected(path, protect); });
            SetStorage(await Task.Run(() => _store.Cleanup(_settings, _sessionPrefix, videoPeakBitrateKbps: StorageVideoPeak(_settings))));
        }
        finally { _gate.Release(); Publish(); }
    }
    public async Task SetLabelAsync(string path, string label)
    {
        await _gate.WaitAsync();
        try { await Task.Run(() => _store.SetLabel(path, label)); _message = "Bezeichnung gespeichert"; }
        finally { _gate.Release(); Publish(); }
    }
    public async Task<int> DeleteRecordingsAsync(IReadOnlyList<string> paths, bool allowProtected = false)
    {
        await _gate.WaitAsync();
        try
        {
            int count = await Task.Run(() => _store.Delete(paths, allowProtected, _sessionPrefix));
            SetStorage(await Task.Run(() => _store.Cleanup(_settings, _sessionPrefix, videoPeakBitrateKbps: StorageVideoPeak(_settings))));
            _message = $"{count} Aufnahme(n) gelöscht";
            return count;
        }
        finally { _gate.Release(); Publish(); }
    }
    public async Task ExportAsync(string path, string destination, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using var readLease = await OpenCompletedRecordingAsync(path, linked.Token);
        try { await Recorder.ExportAsync(readLease.Name, destination, linked.Token); }
        finally { Publish(); }
    }
    public async Task<MediaDetails> GetMediaDetailsAsync(string path, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using var readLease = await OpenCompletedRecordingAsync(path, linked.Token);
        return await MediaTools.ReadDetailsAsync(readLease.Name, linked.Token);
    }
    public async Task ExportClipAsync(string path, string destination, ClipRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using var readLease = await OpenCompletedRecordingAsync(path, linked.Token);
        var details = await MediaTools.ReadDetailsAsync(readLease.Name, linked.Token);
        if (range.Start < TimeSpan.Zero || range.Duration <= TimeSpan.Zero || range.Start >= details.Duration || range.Duration > details.Duration - range.Start)
            throw new ArgumentOutOfRangeException(nameof(range), "Der Ausschnitt muss vollständig innerhalb der Aufnahme liegen.");
        try { await MediaTools.ExportClipAsync(readLease.Name, destination, range, linked.Token); }
        finally { Publish(); }
    }
    private async Task<FileStream> OpenCompletedRecordingAsync(string path, CancellationToken ct)
    {
        // Scan outside the capture gate. Recheck folder/session before leasing the stable file.
        var store = _store; string? prefix = _sessionPrefix;
        var info = (await Task.Run(() => store.List(prefix, ct), ct)).FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Die Aufnahme ist nicht mehr vorhanden.");
        await _gate.WaitAsync(ct);
        try
        {
            if (!ReferenceEquals(store, _store)) throw new InvalidOperationException("Der Aufnahmeordner wurde geändert. Bitte die Liste aktualisieren.");
            if (info.Active || (_sessionPrefix is { } active && Path.GetFileName(info.Path).StartsWith(active + "_", StringComparison.Ordinal)))
                throw new InvalidOperationException("Die aktuelle Aufnahme kann nach Stoppen oder Pausieren exportiert werden.");
            ct.ThrowIfCancellationRequested();
            return new FileStream(info.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        finally { _gate.Release(); }
    }
    public void OpenRecordingFolder()
    {
        Directory.CreateDirectory(_settings.RecordingDirectory);
        Process.Start(new ProcessStartInfo(_settings.RecordingDirectory) { UseShellExecute = true });
    }
    public void OpenLogs()
    {
        Directory.CreateDirectory(AppLog.DirectoryPath);
        Process.Start(new ProcessStartInfo(AppLog.DirectoryPath) { UseShellExecute = true });
    }

    private void OnRecorderChanged(RecorderStatus status)
    {
        bool wasRecording = _recordStatus.Recording;
        _recordStatus = status;
        if (status.Recording) lock (_gameGate) _gameAwaitingFirstCapture = false;
        if (status.Recording && !wasRecording) _recordingClock.Begin();
        if (!status.Recording && wasRecording)
        {
            _sessionPrefix = null;
            _recordingClock.End();
        }
        if (!status.Recording && wasRecording && _session.CanCapture && !_busy && !_disposed)
        {
            _message = FriendlyMessage(status.Message);
            _ = RecoverAsync(_session.Generation);
        }
        Publish();
    }

    private async Task RecoverAsync(long generation)
    {
        // Different user sessions must never share a delayed recovery or stop one another.
        if (Interlocked.Exchange(ref _recoveringGeneration, generation) == generation) return;
        try
        {
            for (int attempt = 1; attempt <= 3 && _session.CanRecover(generation) && !_disposed; attempt++)
            {
                _message = $"Aufnahme unterbrochen · Neustart {attempt}/3 in 5 Sekunden"; Publish();
                await Task.Delay(5000, _lifetime.Token);
                await _gate.WaitAsync(_lifetime.Token);
                try
                {
                    if (!_session.CanRecover(generation) || _disposed) return;
                    await _recorder.StopAsync();
                    try
                    {
                        await StartCoreAsync();
                        if (_recordStatus.Recording) { ShowNotice("Aufnahme nach Unterbrechung fortgesetzt"); return; }
                        AppLog.Write("Encoder unmittelbar nach dem Wiederanlauf beendet; nächster Neustartversuch folgt.");
                    }
                    catch (Exception ex) { AppLog.Write(ex); }
                }
                finally { _gate.Release(); }
            }
            await _gate.WaitAsync(_lifetime.Token);
            try
            {
                if (_session.CanRecover(generation))
                {
                    SuppressGameRestart(); _session.Stop();
                    _message = "Aufnahme nach drei fehlgeschlagenen Neustarts gestoppt. Prüfe Bildschirm/Tongerät und das Protokoll.";
                    ShowNotice(_message); Publish();
                }
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(ex); }
        finally { Interlocked.CompareExchange(ref _recoveringGeneration, -1, generation); }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _locked = true;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect) _locked = false;
        else return;
        SetPrivacyImmediately();
        _ = ApplyPrivacyAsync();
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _suspended = true;
        else if (e.Mode == PowerModes.Resume) _suspended = false;
        else return;
        SetPrivacyImmediately();
        _ = ApplyPrivacyAsync();
    }
    private void SetPrivacyImmediately()
    {
        _session.SetPrivacy(_locked, _suspended, _settings.PauseOnLock);
        if (!_session.CanCapture) CancelPendingStart();
    }
    private void CancelPendingStart() { lock (_startupGate) _pendingStart?.Cancel(); }
    private void HandleCanceledStart()
    {
        if (_session.Paused) _message = "Pausiert · " + _session.PauseReason;
        else if (_session.CanCapture) { _message = "Aufnahmestart unterbrochen · wird erneut geprüft"; _ = ApplyPrivacyAsync(); }
        else _message = "Aufnahme gestoppt";
    }
    private async Task ApplyPrivacyAsync()
    {
        try
        {
            await _gate.WaitAsync(_lifetime.Token);
            try
            {
                _session.SetPrivacy(_locked, _suspended, _settings.PauseOnLock);
                if (_session.Paused && _recordStatus.Recording)
                {
                    await StopCoreAsync("Pausiert · " + _session.PauseReason);
                }
                else if (_session.CanCapture && !_recordStatus.Recording && !_busy)
                {
                    try { await StartCoreAsync(); }
                    catch (GameStartNoLongerEligibleException) { }
                    catch (OperationCanceledException) when (!_disposed) { HandleCanceledStart(); }
                    catch (Exception ex) { SuppressGameRestart(); _session.Stop(); ShowNotice(ex.Message); }
                }
                else if (_session.Paused) _message = "Pausiert · " + _session.PauseReason;
            }
            finally { _gate.Release(); Publish(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private async Task TickAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        int tick = 0;
        try
        {
            // Long disk scans run separately; they cannot hold up the timer or capture controls.
            _backgroundCleanup = RefreshStorageAsync(initial: true);
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                SamplePerformance();
                CheckSilenceWarning();
                tick++;
                if (tick % 3 == 0 && _settings.GameDetectionEnabled && (_gameTask is null || _gameTask.IsCompleted))
                    _gameTask = CheckGamesAsync();
                if (!_busy && _session.Armed && RecordingTimer.HasExpired(_settings.AutoStopMinutes, _recordingClock.Elapsed)
                    && await _gate.WaitAsync(0, _lifetime.Token))
                {
                    try
                    {
                        if (_session.Armed && RecordingTimer.HasExpired(_settings.AutoStopMinutes, _recordingClock.Elapsed))
                        {
                            SuppressGameRestart(); _session.Stop();
                            await StopCoreAsync("Zeitlimit erreicht · Aufnahme automatisch gestoppt");
                            ShowNotice(_message);
                        }
                    }
                    finally { _gate.Release(); }
                }
                // During capture check every 10 s; paused/idle sessions scan only once per minute.
                int checkInterval = _recordStatus.Recording ? 5 : 30;
                if (tick % checkInterval == 0 && !_busy && !_initializing && (_backgroundCleanup is null || _backgroundCleanup.IsCompleted))
                    _backgroundCleanup = RefreshStorageAsync();
                Publish();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshStorageAsync(bool initial = false)
    {
        try
        {
            await _storageOperations.WaitAsync(_lifetime.Token);
            RecordingStore store; CleanupResult result; long revision, generation;
            try
            {
                // Serialize policy changes with destructive cleanup, without holding the capture gate.
                store = _store; var settings = _settings.Clone(); string? prefix = _sessionPrefix;
                revision = Interlocked.Read(ref _storageRevision); generation = _session.Generation;
                result = await Task.Run(() => store.Cleanup(settings, prefix, videoPeakBitrateKbps: StorageVideoPeak(settings)), _lifetime.Token);
            }
            finally { _storageOperations.Release(); }
            await _gate.WaitAsync(_lifetime.Token);
            try
            {
                if (!ReferenceEquals(store, _store) || revision != Interlocked.Read(ref _storageRevision) || generation != _session.Generation) return;
                SetStorage(result);
                if (!_storage.CanRecord && _session.Armed)
                {
                    SuppressGameRestart(); _session.Stop(); CancelPendingStart();
                    await StopCoreAsync(_storage.Message); ShowNotice(_storage.Message);
                }
                else if (!_storage.CanRecord || (initial && _message == "Speicher wird im Hintergrund geprüft …"))
                    _message = _storage.CanRecord ? "Bereit für die erste Aufnahme" : _storage.Message;
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(ex); _message = "Speicherprüfung fehlgeschlagen: " + ex.Message; }
        finally { if (initial) _initializing = false; Publish(); }
    }
    [MemberNotNull(nameof(_storage))]
    private void SetStorage(CleanupResult result) { _storage = result; Interlocked.Increment(ref _storageRevision); }
    private int StorageVideoPeak(AppSettings settings)
    {
        var displays = _displays;
        var display = string.IsNullOrEmpty(settings.DisplayId) ? displays.FirstOrDefault() : displays.FirstOrDefault(item => item.Id == settings.DisplayId);
        int target = settings.AutoVideoBitrate && display is { Width: > 0, Height: > 0 }
            ? RecordingQuality.ResolveBitrateKbps(settings, display)
            : settings.AutoVideoBitrate ? RecordingQuality.MaximumBitrateKbps : settings.VideoBitrateKbps;
        return RecordingQuality.PeakBitrateKbps(target);
    }

    private void SamplePerformance()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var selfCpu = self.TotalProcessorTime;
            var childCpu = TimeSpan.Zero;
            var childId = _recorder.ProcessId;
            long memory = self.WorkingSet64;
            if (childId is int pid)
            {
                try { using var child = Process.GetProcessById(pid); childCpu = child.TotalProcessorTime; memory += child.WorkingSet64; }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            long counter = Stopwatch.GetTimestamp();
            double seconds = (counter - _previousCounter) / (double)Stopwatch.Frequency;
            double deltaCpu = Math.Max(0, (selfCpu - _previousSelfCpu).TotalSeconds);
            if (childId == _previousChildId && childId.HasValue) deltaCpu += Math.Max(0, (childCpu - _previousChildCpu).TotalSeconds);
            _cpu = seconds > 0 ? Math.Clamp(deltaCpu / seconds / Environment.ProcessorCount * 100, 0, 100) : 0;
            _memory = memory; _previousSelfCpu = selfCpu; _previousChildCpu = childCpu; _previousChildId = childId; _previousCounter = counter;
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }
    private void Publish() { if (!_disposed) Changed?.Invoke(); }
    private void ShowNotice(string message) { if (!_disposed && _settings.ShowNotifications) Notice?.Invoke(message); }
    private static string FriendlyMessage(string message)
    {
        int details = message.IndexOf(" Details:", StringComparison.Ordinal);
        if (details >= 0) message = message[..details] + " Details stehen im Protokoll.";
        return message.Length > 600 ? message[..600] + " … Details stehen im Protokoll." : message;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _session.Shutdown();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _recorder.Changed -= OnRecorderChanged;
        _lifetime.Cancel();
        CancelPendingStart();
        _recorder.Dispose();
        // Background tasks wind down through cancellation; don't block the UI message loop.
    }
}
