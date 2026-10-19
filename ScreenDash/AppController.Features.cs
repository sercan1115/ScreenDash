using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace ScreenDash;

public sealed partial class AppController
{
    private readonly ProfileLibrary _profileLibrary;
    private readonly GameDetection _gameDetection;
    private readonly UpdateService _updates;
    private readonly object _gameGate = new();
    private IReadOnlyList<AudioSource> _microphones = [];
    private Task? _gameTask;
    private bool _gameRecordingOwned;
    private long? _gameExitCounter, _silentSinceCounter;
    private string _silenceWarning = "";
    private string _gameDetectionMessage = "Warte auf ein ausgewähltes Spiel im Vordergrund.";
    public IReadOnlyList<AudioSource> Microphones => _microphones;

    public async Task AddBookmarkAsync(string label = "")
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            var prefix = _sessionPrefix;
            var elapsed = _recordStatus.VideoElapsed;
            if (!_recordStatus.Recording || prefix is null) throw new InvalidOperationException("Ein Lesezeichen kann während einer laufenden Aufnahme gesetzt werden.");
            var file = (await Task.Run(() => _store.List(prefix), _lifetime.Token))
                .Where(f => f.Active && f.FileName.StartsWith(prefix + "_", StringComparison.Ordinal)).OrderByDescending(f => f.FileName, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("Die Aufnahmedatei wird noch angelegt. Bitte kurz warten.");
            var offset = elapsed;
            if (!_settings.SingleFile && int.TryParse(Path.GetFileNameWithoutExtension(file.Path).Split('_').Last(), NumberStyles.None, CultureInfo.InvariantCulture, out int segment))
                offset -= TimeSpan.FromMinutes((long)segment * _settings.SegmentMinutes);
            if (offset < TimeSpan.Zero) offset = TimeSpan.Zero;
            await Task.Run(() => { _store.SetProtected(file.Path, true); _store.AddBookmark(file.Path, offset, label); }, _lifetime.Token);
            _message = $"Lesezeichen bei {offset:hh\\:mm\\:ss} gespeichert · ganze Datei geschützt";
            ShowNotice(_message);
        }
        finally { _gate.Release(); Publish(); }
    }

    public async Task<IReadOnlyList<RecordingBookmark>> GetBookmarksAsync(string path, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var store = _store;
        return await Task.Run(() => store.GetBookmarks(path, linked.Token), linked.Token);
    }
    public async Task RenameBookmarkAsync(string path, string id, string label)
    {
        await _gate.WaitAsync(_lifetime.Token);
        try { await Task.Run(() => _store.RenameBookmark(path, id, label), _lifetime.Token); _message = "Lesezeichen umbenannt"; }
        finally { _gate.Release(); Publish(); }
    }
    public async Task DeleteBookmarkAsync(string path, string id)
    {
        await _gate.WaitAsync(_lifetime.Token);
        try { await Task.Run(() => _store.DeleteBookmark(path, id), _lifetime.Token); _message = "Lesezeichen entfernt · Dateischutz bleibt bestehen"; }
        finally { _gate.Release(); Publish(); }
    }
    public async Task<RecordingTimeMatch?> FindByTimeAsync(DateTime localTime, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var utc = localTime.ToUniversalTime();
        var files = await GetRecordingsAsync(linked.Token);
        // The recorder cannot overlap its own files. Probe the nearest preceding file;
        // tolerate only two unreadable candidates, never launch ffprobe for the entire archive.
        foreach (var file in files.Where(f => f.StartedUtc <= utc).OrderByDescending(f => f.StartedUtc).Take(3))
        {
            linked.Token.ThrowIfCancellationRequested();
            var offset = utc - file.StartedUtc;
            TimeSpan duration;
            if (file.Active)
            {
                var prefix = _sessionPrefix;
                if (prefix is null || !file.FileName.StartsWith(prefix + "_", StringComparison.Ordinal)) continue;
                duration = _recordStatus.VideoElapsed;
                if (!_settings.SingleFile && int.TryParse(Path.GetFileNameWithoutExtension(file.Path).Split('_').Last(), out int segment))
                    duration -= TimeSpan.FromMinutes((long)segment * _settings.SegmentMinutes);
            }
            else
            {
                try { duration = (await GetMediaDetailsAsync(file.Path, linked.Token)).Duration; }
                catch (Exception ex) when (ex is IOException or InvalidOperationException) { AppLog.Write(ex); continue; }
            }
            if (offset >= TimeSpan.Zero && offset < duration) return new(file, offset);
            return null;
        }
        return null;
    }
    public async Task<IReadOnlyList<RecordingProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await Task.Run(_profileLibrary.Load, linked.Token);
    }
    public async Task SaveProfileAsync(string name, AppSettings settings)
    {
        await Task.Run(() => _profileLibrary.Save(name, settings), _lifetime.Token);
        _message = "Aufnahmeprofil gespeichert"; Publish();
    }
    public async Task ApplyProfileAsync(string id)
    {
        var profile = (await GetProfilesAsync()).FirstOrDefault(p => p.Id == id) ?? throw new FileNotFoundException("Das Profil ist nicht mehr vorhanden.");
        await SaveSettingsAsync(ProfileLibrary.Apply(Settings, profile));
    }
    public async Task DeleteProfileAsync(string id) => await Task.Run(() => _profileLibrary.Delete(id), _lifetime.Token);
    public async Task<IReadOnlyList<string>> GetRunningGamesAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await Task.Run(_gameDetection.GetRunningPrograms, linked.Token);
    }
    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        return await _updates.CheckAsync(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 4, 0), linked.Token);
    }
    public async Task<PreparedUpdate> PrepareUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenDash", "updates");
        return await _updates.PrepareAsync(info, root, progress, linked.Token);
    }
    public void LaunchPreparedUpdate(PreparedUpdate update)
    {
        if (_session.Armed || _recordStatus.Recording || _busy) throw new InvalidOperationException("Stoppe zuerst die Aufnahme, bevor du die neue Version startest.");
        _updates.LaunchInstaller(update, Environment.ProcessId, _settings.StartWithWindows, Environment.GetCommandLineArgs().Contains("--tray", StringComparer.OrdinalIgnoreCase));
    }

    private void SuppressGameRestart()
    {
        lock (_gameGate) { _gameDetection.MarkRecordingStopped(); _gameDetection.SuppressUntilGamesExit(); _gameRecordingOwned = false; _gameExitCounter = null; }
    }
    private async Task CheckGamesAsync()
    {
        try
        {
            var settings = _settings.Clone(); long stopRequest = Interlocked.Read(ref _stopRequests);
            if (!settings.GameDetectionEnabled) return;
            var result = await Task.Run(() => _gameDetection.Evaluate(settings.GameExecutables), _lifetime.Token);
            if (!await _gate.WaitAsync(0, _lifetime.Token)) return;
            try
            {
                if (!_settings.GameDetectionEnabled || !settings.GameExecutables.SequenceEqual(_settings.GameExecutables) || stopRequest != Interlocked.Read(ref _stopRequests) || _busy) return;
                _gameDetectionMessage = result.Diagnostic;
                // A failed snapshot must not count toward the ten-second confirmed-exit grace.
                if (!result.SnapshotReliable) { _gameExitCounter = null; return; }
                if (result.HasGames) _gameExitCounter = null;
                if (result.ShouldStart && !_session.Armed && !_recordStatus.Recording && !_locked && !_suspended)
                {
                    lock (_gameGate)
                    {
                        if (stopRequest != Interlocked.Read(ref _stopRequests)) return;
                        _session.Start(); _recordingClock.Reset();
                        // Ownership follows intent, including a startup paused by lock/standby.
                        _gameDetection.MarkRecordingStarted(); _gameRecordingOwned = true;
                    }
                    if (stopRequest != Interlocked.Read(ref _stopRequests)) { _session.Stop(); SuppressGameRestart(); return; }
                    try
                    {
                        await StartCoreAsync();
                        if (_recordStatus.Recording) { _message = "Spiel erkannt · " + string.Join(", ", result.RunningGames); ShowNotice(_message); }
                    }
                    catch (OperationCanceledException) when (!_disposed) { HandleCanceledStart(); }
                    catch (Exception ex) { _session.Stop(); SuppressGameRestart(); AppLog.Write(ex); ShowNotice("Spielaufnahme konnte nicht starten: " + FriendlyMessage(ex.Message)); }
                }
                else if (result.ShouldStop && _gameRecordingOwned && _session.Armed)
                {
                    _gameExitCounter ??= Stopwatch.GetTimestamp();
                    if (Stopwatch.GetElapsedTime(_gameExitCounter.Value) >= TimeSpan.FromSeconds(10))
                    {
                        lock (_gameGate) { _gameDetection.MarkRecordingStopped(); _gameRecordingOwned = false; _gameExitCounter = null; }
                        _session.Stop(); CancelPendingStart(); await StopCoreAsync("Spiel beendet · Aufnahme automatisch gestoppt"); ShowNotice(_message);
                    }
                }
            }
            finally { _gate.Release(); Publish(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(ex); }
    }
    private void CheckSilenceWarning()
    {
        var status = _recordStatus;
        if (!status.Recording || !_settings.AudioEnabled || _settings.SystemGainPercent == 0 || !_settings.WarnOnSilence || status.AudioPeak > 0.0001)
        { _silentSinceCounter = null; _silenceWarning = ""; return; }
        _silentSinceCounter ??= Stopwatch.GetTimestamp();
        if (_silenceWarning.Length == 0 && Stopwatch.GetElapsedTime(_silentSinceCounter.Value).TotalSeconds >= _settings.SilenceWarningSeconds)
        {
            _silenceWarning = "Kein Spielton erkannt · prüfe das gewählte Wiedergabegerät und die Lautstärke im Spiel.";
            ShowNotice(_silenceWarning);
        }
    }
}
