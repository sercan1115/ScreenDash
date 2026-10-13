using System.Diagnostics;
using Microsoft.Win32;

namespace ScreenDash;

public sealed class AppController : IAppController
{
    private readonly Recorder _recorder = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _ticker;
    private readonly RecordingSessionState _session = new();
    private RecordingStore _store;
    private AppSettings _settings;
    private IReadOnlyList<DisplaySource> _displays;
    private IReadOnlyList<AudioSource> _audioDevices;
    private RecorderStatus _recordStatus = new(false, "–", "Bereit", 0, 0, null);
    private CleanupResult? _storage;
    private string _message = "Bereit für die erste Aufnahme";
    private bool _busy, _locked, _suspended, _disposed;
    private long _recoveringGeneration = -1;
    private DateTime? _started;
    private TimeSpan _completedElapsed;
    private double _cpu;
    private long _memory;
    private TimeSpan _previousSelfCpu, _previousChildCpu;
    private int? _previousChildId;
    private long _previousCounter = Stopwatch.GetTimestamp();
    private string? _sessionPrefix;

    public AppSettings Settings => _settings.Clone();
    public IReadOnlyList<DisplaySource> Displays => _displays;
    public IReadOnlyList<AudioSource> AudioDevices => _audioDevices;
    public DashboardStatus Status => new(_recordStatus.Recording, _busy, _message, _recordStatus.Encoder,
        _session.Armed ? _completedElapsed + (_started.HasValue && _recordStatus.Recording ? TimeSpan.FromTicks(Math.Max(0, (DateTime.UtcNow - _started.Value).Ticks)) : TimeSpan.Zero) : TimeSpan.Zero,
        _cpu, _memory, _recordStatus.Fps, _storage, _session.Armed, _session.Paused, _session.PauseReason);
    public event Action? Changed;
    public event Action<string>? Notice;

    public AppController(AppSettings? initialSettings = null)
    {
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
        _session.SetPrivacy(false, false, _settings.PauseOnLock);
        _storage = _store.Cleanup(_settings);
        _recorder.Changed += OnRecorderChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _ticker = Task.Run(TickAsync);
    }

    public async Task ToggleRecordingAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_session.Armed || _recordStatus.Recording)
            {
                _session.Stop();
                await StopCoreAsync("Aufnahme gestoppt");
            }
            else
            {
                _session.Start();
                _completedElapsed = TimeSpan.Zero;
                try { await StartCoreAsync(); }
                catch { _session.Stop(); throw; }
            }
        }
        finally { _gate.Release(); Publish(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { _session.Stop(); await StopCoreAsync("Aufnahme gestoppt"); }
        finally { _gate.Release(); Publish(); }
    }

    public async Task PauseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _session.Pause();
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
                catch { _session.Pause(); throw; }
            }
            else _message = "Pausiert · " + _session.PauseReason;
        }
        finally { _gate.Release(); Publish(); }
    }

    private async Task StartCoreAsync()
    {
        _busy = true; _message = "Grafik-Encoder wird geprüft …"; Publish();
        bool startupCompleted = false;
        try
        {
            _settings = SettingsStore.Validate(_settings);
            _displays = DisplayCatalog.GetDisplays();
            var display = string.IsNullOrEmpty(_settings.DisplayId) ? _displays.FirstOrDefault() : _displays.FirstOrDefault(d => d.Id == _settings.DisplayId);
            if (display is null) throw new InvalidOperationException("Der gewählte Bildschirm ist nicht verbunden. Wähle in den Einstellungen einen verfügbaren Bildschirm.");
            _storage = await Task.Run(() => _store.Cleanup(_settings));
            if (!_storage.CanRecord) throw new InvalidOperationException(_storage.Message);
            _sessionPrefix = "screendash_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'") + "_" + Guid.NewGuid().ToString("N")[..8];
            await _recorder.StartAsync(_settings.Clone(), display, _sessionPrefix, _lifetime.Token);
            if (!_recorder.Status.Recording) throw new IOException("Die Aufnahme wurde bereits beim Start unterbrochen. Prüfe das Protokoll und die Geräte.");
            startupCompleted = true;
            _started = _recorder.Status.StartedUtc ?? DateTime.UtcNow;
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
            _busy = false;
            if (startupCompleted && !_recordStatus.Recording && _session.CanCapture && !_disposed) _ = RecoverAsync(_session.Generation);
            Publish();
        }
    }

    private async Task StopCoreAsync(string message)
    {
        _busy = true; Publish();
        try { await _recorder.StopAsync(); _sessionPrefix = null; _started = null; _message = message; }
        finally { _busy = false; Publish(); }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var validated = SettingsStore.Validate(settings);
        await _gate.WaitAsync();
        try
        {
            if (_session.Armed || _recordStatus.Recording || _busy) throw new InvalidOperationException("Stoppe die Aufnahme, bevor du Einstellungen änderst.");
            // Validate folder access before touching settings/startup; commit the live pair together.
            var candidateStore = new RecordingStore(validated.RecordingDirectory);
            using var startupKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            var previousStartup = startupKey.GetValue("ScreenDash", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var previousKind = previousStartup is null ? RegistryValueKind.String : startupKey.GetValueKind("ScreenDash");
            try
            {
                SetWindowsStartup(validated.StartWithWindows);
                SettingsStore.Save(validated);
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
            _settings = validated;
            _session.SetPrivacy(_locked, _suspended, validated.PauseOnLock);
            _storage = _store.Cleanup(validated);
            _message = "Einstellungen gespeichert";
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
        _displays = displays; _audioDevices = audio; Publish();
    }

    public async Task ProtectRecentAsync()
    {
        await _gate.WaitAsync();
        try
        {
            int count = await Task.Run(() => _store.ProtectRecent(_settings.ProtectMinutes, _sessionPrefix));
            _storage = _store.Cleanup(_settings, _sessionPrefix);
            _message = count > 0 ? $"{count} Videodatei(en) vor automatischem Löschen geschützt" : "Keine Aufnahmen im gewählten Zeitraum vorhanden";
            ShowNotice(_message);
        }
        finally { _gate.Release(); Publish(); }
    }

    public IReadOnlyList<RecordingInfo> GetRecordings() => _store.List(_sessionPrefix);
    public async Task SetProtectedAsync(string path, bool protect)
        => await SetProtectedManyAsync([path], protect);
    public async Task SetProtectedManyAsync(IReadOnlyList<string> paths, bool protect)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(() => { foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) _store.SetProtected(path, protect); });
            _storage = _store.Cleanup(_settings, _sessionPrefix);
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
            _storage = _store.Cleanup(_settings, _sessionPrefix);
            _message = $"{count} Aufnahme(n) gelöscht";
            return count;
        }
        finally { _gate.Release(); Publish(); }
    }
    public async Task ExportAsync(string path, string destination)
    {
        // A read lease prevents deletion while exporting without changing the user's protection setting.
        await _gate.WaitAsync();
        FileStream readLease;
        try
        {
            var info = _store.List(_sessionPrefix).FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Die Aufnahme ist nicht mehr vorhanden.");
            if (info.Active) throw new InvalidOperationException("Die aktuelle Videodatei kann nach dem Segmentwechsel oder Stoppen exportiert werden.");
            readLease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        finally { _gate.Release(); }
        using (readLease) await Recorder.ExportAsync(path, destination, _lifetime.Token);
        Publish();
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
        if (!status.Recording && wasRecording)
        {
            _sessionPrefix = null;
            if (_started is DateTime started) _completedElapsed += TimeSpan.FromTicks(Math.Max(0, (DateTime.UtcNow - started).Ticks));
            _started = null;
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
                    _session.Stop();
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
        _ = ApplyPrivacyAsync();
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _suspended = true;
        else if (e.Mode == PowerModes.Resume) _suspended = false;
        else return;
        _ = ApplyPrivacyAsync();
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
                    catch (Exception ex) { _session.Stop(); ShowNotice(ex.Message); }
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
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                SamplePerformance();
                tick++;
                // During capture check every 10 s; paused/idle sessions scan only once per minute.
                int checkInterval = _recordStatus.Recording ? 5 : 30;
                if (tick % checkInterval == 0 && !_busy && await _gate.WaitAsync(0, _lifetime.Token))
                {
                    try
                    {
                        _storage = _store.Cleanup(_settings, _sessionPrefix);
                        if (!_storage.CanRecord && _session.Armed)
                        {
                            _session.Stop();
                            await StopCoreAsync(_storage.Message); ShowNotice(_storage.Message);
                        }
                    }
                    catch (Exception ex) { AppLog.Write(ex); _message = "Speicherprüfung fehlgeschlagen: " + ex.Message; }
                    finally { _gate.Release(); }
                }
                Publish();
            }
        }
        catch (OperationCanceledException) { }
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
        _recorder.Dispose();
        // Background tasks wind down through cancellation; don't block the UI message loop.
    }
}
