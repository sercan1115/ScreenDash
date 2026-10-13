using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ScreenDash;

/// <summary>Desktop Duplication + hardware H.264, segmented Matroska, and bounded playback loopback.</summary>
public sealed class Recorder : IDisposable
{
    private sealed record Encoder(string Name, string Codec, string VideoFilters, string[] Options);
    private static readonly Encoder Nvenc = new("NVIDIA NVENC", "h264_nvenc", "", ["-preset", "p1", "-tune", "ll", "-rc", "vbr"]);
    private static readonly Encoder Amf = new("AMD AMF", "h264_amf", "", ["-usage", "lowlatency", "-quality", "speed", "-rc", "vbr_peak"]);
    private static readonly Encoder Qsv = new("Intel Quick Sync", "h264_qsv", ",hwmap=derive_device=qsv,format=qsv,vpp_qsv=format=nv12", ["-preset", "veryfast"]);
    private static readonly Encoder Software = new("CPU · Software", "libx264", ",hwdownload,format=bgra,format=yuv420p", ["-preset", "ultrafast", "-tune", "zerolatency", "-threads", "2"]);
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _errorLock = new();
    private readonly Queue<string> _stderr = new();
    private volatile RecorderStatus _status = new(false, "", "Bereit", 0, 0, null);
    private Process? _process;
    private AudioLoopback? _audio;
    private Task? _stderrTask;
    private Task? _progressTask;
    private CancellationTokenSource? _startCancellation;
    private TaskCompletionSource<bool>? _firstFrame;
    private volatile bool _intentionalStop;
    private int _disposed;
    private string? _failure;
    private static readonly object LogLock = new();

    public event Action<RecorderStatus>? Changed;
    public RecorderStatus Status => _status;
    public int? ProcessId
    {
        get
        {
            var process = _process;
            try { return process is { HasExited: false } ? process.Id : null; } catch { return null; }
        }
    }
    public string? SessionPrefix { get; private set; }

    public static string FindFfmpeg()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe")
        }) if (File.Exists(candidate)) return candidate;
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var directory = entry.Trim().Trim('"');
                if (directory.Length == 0) continue;
                var candidate = Path.Combine(directory, "ffmpeg.exe");
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (ArgumentException) { }
        }
        throw new FileNotFoundException("FFmpeg fehlt. Bitte ffmpeg.exe in den Programmordner oder dessen Unterordner tools kopieren.");
    }

    public async Task StartAsync(AppSettings settings, DisplaySource display, string sessionPrefix, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        var ownsStartup = false;
        try
        {
            if (_process is not null) throw new InvalidOperationException("Es läuft bereits eine Aufnahme.");
            ownsStartup = true;
            _startCancellation = startup;
            Validate(settings, display, sessionPrefix);
            var options = settings.Clone();
            options.RecordingDirectory = Path.GetFullPath(options.RecordingDirectory);
            Directory.CreateDirectory(options.RecordingDirectory);
            if (Directory.EnumerateFiles(options.RecordingDirectory, sessionPrefix + "_*.mkv").Any())
                throw new IOException("Der Dateiname dieser Aufnahmesitzung wird bereits verwendet.");
            var executable = FindFfmpeg();
            _failure = null;
            _intentionalStop = false;
            lock (_errorLock) _stderr.Clear();
            Publish(new(false, "", "Hardware-Encoder wird mit dem ausgewählten Bildschirm geprüft …", 0, 0, null));
            var encoder = await SelectEncoderAsync(executable, options, display, startup.Token).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();
            // Probing can take several seconds. A hotplug may reuse an adapter/output index
            // during that interval; never start recording a newly substituted desktop.
            DisplayCatalog.ValidateSelection(display, DisplayCatalog.GetDisplays());
            SessionPrefix = sessionPrefix;
            _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (options.AudioEnabled)
            {
                _audio = new AudioLoopback(options.AudioDeviceId);
                _audio.Faulted += AudioFailed;
            }
            // Both streams use this epoch. Audio is numbered continuously from zero, including silent gaps.
            var startedUtc = DateTime.UtcNow;
            var originMicroseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            _audio?.Start();
            var args = BuildArguments(options, display, encoder, _audio, sessionPrefix, originMicroseconds, false);
            var process = new Process { StartInfo = CreateStartInfo(executable, args) };
            _process = process;
            if (!process.Start()) throw new IOException("FFmpeg konnte nicht gestartet werden.");
            LowerPriority(process);
            _stderrTask = ReadErrorsAsync(process);
            _progressTask = ReadProgressAsync(process, encoder.Name, startedUtc, _firstFrame);
            Publish(new(false, encoder.Name, "Aufnahme wird gestartet …", 0, 0, startedUtc));
            var exited = process.WaitForExitAsync(startup.Token);
            var deadline = Task.Delay(TimeSpan.FromSeconds(15), startup.Token);
            var winner = await Task.WhenAny(_firstFrame.Task, exited, deadline).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();
            if (winner != _firstFrame.Task)
            {
                if (process.HasExited)
                {
                    await _stderrTask.ConfigureAwait(false);
                    throw new IOException("Aufnahme konnte nicht starten: " + LastError());
                }
                throw new TimeoutException("Die Aufnahme liefert keine Bilder. Bildschirm und Audiogerät prüfen.");
            }
            await _firstFrame.Task.ConfigureAwait(false);
            if (process.HasExited) throw new IOException("Aufnahme wurde beim Start beendet: " + LastError());
            Publish(_status with { Recording = true, Message = "Aufnahme läuft" });
            _ = ObserveExitAsync(process);
        }
        catch (Exception ex)
        {
            if (!ownsStartup) throw;
            _intentionalStop = true;
            await StopResourcesAsync().ConfigureAwait(false);
            SessionPrefix = null;
            Publish(new(false, _status.Encoder, ex is OperationCanceledException ? "Start abgebrochen" : ex.Message, 0, 0, null));
            throw;
        }
        finally
        {
            if (ownsStartup) _startCancellation = null;
            _lifecycle.Release();
        }
    }

    private async Task<Encoder> SelectEncoderAsync(string executable, AppSettings settings, DisplaySource display, CancellationToken ct)
    {
        var mode = settings.EncoderMode.Trim().ToLowerInvariant();
        var candidates = mode switch
        {
            "auto" => new[] { Nvenc, Amf, Qsv },
            "nvenc" or "nvidia" or "h264_nvenc" => new[] { Nvenc },
            "amf" or "amd" or "h264_amf" => new[] { Amf },
            "qsv" or "intel" or "h264_qsv" => new[] { Qsv },
            "software" or "cpu" => new[] { Software },
            _ => throw new ArgumentException("Unbekannter Encoder-Modus.")
        };
        var errors = new List<string>();
        foreach (var encoder in candidates)
        {
            ct.ThrowIfCancellationRequested();
            Publish(_status with { Encoder = encoder.Name, Message = $"Prüfe {encoder.Name} …" });
            var result = await ProbeAsync(executable, BuildArguments(settings, display, encoder, null, "", 0, true), ct).ConfigureAwait(false);
            if (result.Success) return encoder;
            errors.Add(encoder.Name + ": " + result.Error);
            WriteLog("Encoder probe " + encoder.Name + ": " + result.Error);
        }
        if (mode == "auto" && settings.AllowSoftwareEncoding)
        {
            Publish(_status with { Encoder = Software.Name, Message = "Prüfe ausdrücklich erlaubte CPU-Aufnahme …" });
            var result = await ProbeAsync(executable, BuildArguments(settings, display, Software, null, "", 0, true), ct).ConfigureAwait(false);
            if (result.Success) return Software;
            errors.Add(Software.Name + ": " + result.Error);
        }
        var message = mode == "auto" && !settings.AllowSoftwareEncoding
            ? "Kein passender Hardware-Encoder verfügbar. GPU-Treiber prüfen oder CPU-Aufnahme ausdrücklich in den Einstellungen erlauben."
            : "Der ausgewählte Encoder kann diesen Bildschirm nicht aufnehmen. GPU-Treiber und Bildschirm prüfen.";
        throw new InvalidOperationException(message + " Details: " + string.Join(" | ", errors));
    }

    private static List<string> BuildArguments(AppSettings s, DisplaySource display, Encoder encoder,
        AudioLoopback? audio, string prefix, long originMicroseconds, bool probe)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-nostats", "-n",
            "-init_hw_device", $"d3d11va=desktop:{display.AdapterIndex}",
            "-filter_hw_device", "desktop", "-filter_complex_threads", "1", "-filter_threads", "1"
        };
        if (probe) args.Add("-nostdin");
        else args.AddRange(["-stats_period", "1", "-progress", "pipe:1"]);
        if (audio is not null)
        {
            args.AddRange(["-thread_queue_size", "64", "-probesize", "32", "-analyzeduration", "0",
                "-f", audio.RawFormat, "-ar", audio.SampleRate.ToString(CultureInfo.InvariantCulture),
                "-ac", audio.Channels.ToString(CultureInfo.InvariantCulture), "-i", audio.PipePath]);
        }
        var graph = $"ddagrab=output_idx={display.OutputIndex}:framerate={s.Fps}:draw_mouse={(s.CaptureCursor ? 1 : 0)}:output_fmt=bgra";
        // Align the first frame to the audio epoch, then use ddagrab's monotonic clock.
        // A later Windows clock correction must not jump the video's timestamps.
        graph += probe ? ",setpts=PTS-STARTPTS"
            : $",setpts='if(eq(N,0),(RTCTIME-{originMicroseconds})/(1000000*TB),PREV_OUTPTS+PTS-PREV_INPTS)'";
        graph += encoder.VideoFilters + "[screen]";
        args.AddRange(["-filter_complex", graph, "-map", "[screen]"]);
        if (audio is not null)
            args.AddRange(["-map", "0:a:0", "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-ar", "48000"]);
        else args.Add("-an");
        args.AddRange(["-c:v", encoder.Codec]);
        args.AddRange(encoder.Options);
        args.AddRange(["-b:v", $"{s.VideoBitrateKbps}k", "-maxrate", $"{s.VideoBitrateKbps}k", "-bufsize", $"{s.VideoBitrateKbps * 2}k",
            "-g", (s.Fps * 2).ToString(CultureInfo.InvariantCulture), "-bf", "0", "-enc_time_base:v", "filter",
            "-fps_mode", "passthrough"]);
        if (probe) args.AddRange(["-frames:v", "4", "-f", "null", "-"]);
        else if (s.SingleFile)
        {
            args.AddRange(["-f", "matroska", "-cluster_time_limit", "2000", "-flush_packets", "1",
                Path.Combine(s.RecordingDirectory, prefix + "_000000.mkv")]);
        }
        else
        {
            args.AddRange(["-f", "segment", "-segment_time", (s.SegmentMinutes * 60).ToString(CultureInfo.InvariantCulture),
                "-segment_format", "matroska", "-reset_timestamps", "1", "-segment_format_options", "cluster_time_limit=2000:flush_packets=1",
                Path.Combine(s.RecordingDirectory, prefix + "_%06d.mkv")]);
        }
        return args;
    }

    private static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static async Task<(bool Success, string Error)> ProbeAsync(string executable, List<string> args, CancellationToken ct)
    {
        using var process = new Process { StartInfo = CreateStartInfo(executable, args) };
        if (!process.Start()) return (false, "FFmpeg startet nicht");
        LowerPriority(process);
        var errors = ReadBoundedAsync(process.StandardError);
        var output = ReadBoundedAsync(process.StandardOutput);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);
            await output.ConfigureAwait(false);
            return (process.ExitCode == 0, error.Length == 0 ? $"Exit-Code {process.ExitCode}" : error);
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(process).ConfigureAwait(false);
            await Task.WhenAll(errors, output).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return (false, "Hardware-Prüfung überschritt 6 Sekunden");
        }
    }

    private async Task ReadErrorsAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length > 1500) line = line[^1500..];
                lock (_errorLock)
                {
                    _stderr.Enqueue(line);
                    while (_stderr.Count > 12) _stderr.Dequeue();
                }
                WriteLog(line);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ReadProgressAsync(Process process, string encoder, DateTime startedUtc, TaskCompletionSource<bool> firstFrame)
    {
        long frames = 0;
        double fps = 0;
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;
                var key = line[..separator];
                var value = line[(separator + 1)..];
                if (key == "frame" && long.TryParse(value, out var parsedFrames)) frames = parsedFrames;
                else if (key == "fps" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFps)) fps = parsedFps;
                else if (key == "progress")
                {
                    if (ReferenceEquals(_process, process))
                    {
                        if (frames > 0) firstFrame.TrySetResult(true);
                        Publish(new(!_intentionalStop && frames > 0, encoder, _intentionalStop ? "Aufnahme wird beendet …" : "Aufnahme läuft", fps, frames, startedUtc));
                    }
                }
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ObserveExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_process, process)) return;
                if (_intentionalStop) return;
                if (_stderrTask is not null) await _stderrTask.ConfigureAwait(false);
                var error = _failure ?? "Aufnahme unerwartet beendet: " + LastError();
                await StopResourcesAsync().ConfigureAwait(false);
                SessionPrefix = null;
                Publish(_status with { Recording = false, Message = error });
            }
            finally { _lifecycle.Release(); }
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { WriteLog("Exit observer: " + ex.Message); }
    }

    private void AudioFailed(Exception ex)
    {
        if (_intentionalStop || IsExited(_process)) return;
        _failure = "Audioaufnahme fehlgeschlagen: " + ex.Message;
        WriteLog(_failure);
        _firstFrame?.TrySetException(new IOException(_failure, ex));
        _ = StopAfterFailureAsync();
    }

    private async Task StopAfterFailureAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
            if (_failure is { } message) Publish(_status with { Message = message });
        }
        catch (Exception ex) { WriteLog("Audio shutdown: " + ex.Message); }
    }

    public async Task StopAsync()
    {
        try { _startCancellation?.Cancel(); } catch (ObjectDisposedException) { }
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _intentionalStop = true;
            if (_process is not null) Publish(_status with { Message = "Aufnahme wird beendet …" });
            await StopResourcesAsync().ConfigureAwait(false);
            SessionPrefix = null;
            Publish(_status with { Recording = false, Message = _failure ?? "Aufnahme beendet", Fps = 0 });
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopResourcesAsync()
    {
        var process = _process;
        if (process is not null)
        {
            try
            {
                if (!IsExited(process))
                {
                    using var gracefulStop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    // The deadline also covers stdin: a stuck encoder must not hold shutdown
                    // indefinitely before we even reach WaitForExitAsync.
                    await process.StandardInput.WriteLineAsync("q".AsMemory(), gracefulStop.Token).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync(gracefulStop.Token).ConfigureAwait(false);
                    await process.WaitForExitAsync(gracefulStop.Token).ConfigureAwait(false);
                }
            }
            catch { Kill(process); }
            if (!IsExited(process))
            {
                try { await TerminateAsync(process).ConfigureAwait(false); } catch (Exception ex) { WriteLog("Encoder shutdown: " + ex.Message); }
            }
        }
        var audio = _audio;
        _audio = null;
        if (audio is not null)
        {
            audio.Faulted -= AudioFailed;
            await audio.StopAsync().ConfigureAwait(false);
            audio.Dispose();
        }
        if (_stderrTask is not null) { try { await _stderrTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { } }
        if (_progressTask is not null) { try { await _progressTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { } }
        _process = null;
        _stderrTask = null;
        _progressTask = null;
        process?.Dispose();
    }

    public static async Task ExportAsync(string source, string destination, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (!File.Exists(source)) throw new FileNotFoundException("Die Aufnahme wurde nicht gefunden.", source);
        if (!string.Equals(Path.GetExtension(destination), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Für den Export bitte eine .mp4-Datei wählen.");
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Quelle und Ziel müssen verschieden sein.");
        if (File.Exists(destination)) throw new IOException("Die Zieldatei existiert bereits. Bitte einen neuen Namen wählen.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".screendash-export-" + Guid.NewGuid().ToString("N") + ".mp4");
        using var process = new Process { StartInfo = CreateStartInfo(FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", temporary]) };
        try
        {
            if (!process.Start()) throw new IOException("FFmpeg konnte für den Export nicht gestartet werden.");
            LowerPriority(process);
            var errors = ReadBoundedAsync(process.StandardError);
            var output = ReadBoundedAsync(process.StandardOutput);
            try { await process.WaitForExitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                await TerminateAsync(process).ConfigureAwait(false);
                await Task.WhenAll(errors, output).ConfigureAwait(false);
                throw;
            }
            var error = await errors.ConfigureAwait(false);
            await output.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Export fehlgeschlagen: " + error);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string LastError()
    {
        lock (_errorLock)
        {
            var result = string.Join(" ", _stderr.TakeLast(4));
            return result.Length == 0 ? "FFmpeg hat keine Fehlerbeschreibung geliefert." : result;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var tail = new StringBuilder();
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            tail.Append(buffer, 0, read);
            if (tail.Length > 4000) tail.Remove(0, tail.Length - 4000);
        }
        return tail.ToString().Trim();
    }

    private static void LowerPriority(Process process)
    {
        ChildProcessGuard.Attach(process);
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }
    private static async Task TerminateAsync(Process process)
    {
        Kill(process);
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException ex) { throw new IOException("FFmpeg lässt sich nicht beenden. Bitte das Aufnahmeprotokoll prüfen.", ex); }
    }
    private static bool IsExited(Process? process)
    {
        if (process is null) return false;
        try { return process.HasExited; } catch (InvalidOperationException) { return true; }
    }
    private void Publish(RecorderStatus status)
    {
        if (status.Recording && _audio is { } audio)
            status = status with { AudioPeak = audio.PeakLevel, AudioDevice = audio.DeviceName };
        _status = status;
        try { Changed?.Invoke(status); } catch (Exception ex) { WriteLog("Status observer: " + ex.Message); }
    }
    private static void Validate(AppSettings s, DisplaySource display, string prefix)
    {
        if (s.Fps is < 1 or > 60 || s.VideoBitrateKbps is < 250 or > 100000 || s.SegmentMinutes is < 1 or > 60)
            throw new ArgumentException("Ungültige Bildrate, Bitrate oder Segmentlänge.");
        if (display.AdapterIndex < 0 || display.OutputIndex < 0 || display.Width <= 0 || display.Height <= 0)
            throw new ArgumentException("Ungültiger Bildschirm.");
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Ungültiger Dateiname der Aufnahmesitzung.");
    }
    private static void WriteLog(string message)
    {
        try
        {
            lock (LogLock)
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenDash", "logs");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "ffmpeg.log");
                if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024)
                    File.Move(file, file + ".old", true);
                File.AppendAllText(file, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
            }
        }
        catch { /* A log failure must not interrupt recording. */ }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopAsync().GetAwaiter().GetResult();
        // Exit observers can still be queued; do not dispose their semaphore underneath them.
    }
}
