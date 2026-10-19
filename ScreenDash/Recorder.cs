using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ScreenDash;

/// <summary>Desktop Duplication + hardware H.264, Matroska, and bounded optional WASAPI audio sources.</summary>
public sealed class Recorder : IDisposable
{
    private sealed record Encoder(string Name, string Codec, string VideoFilters);
    private static readonly Encoder Nvenc = new("NVIDIA NVENC", "h264_nvenc", "");
    private static readonly Encoder Amf = new("AMD AMF", "h264_amf", "");
    private static readonly Encoder Qsv = new("Intel Quick Sync", "h264_qsv", ",hwmap=derive_device=qsv,format=qsv,vpp_qsv=format=nv12");
    private static readonly Encoder Software = new("CPU · Software", "libx264", ",hwdownload,format=bgra,format=yuv420p");
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _errorLock = new();
    private readonly Func<IReadOnlyList<DisplaySource>> _getDisplays;
    private readonly Queue<string> _stderr = new();
    private volatile RecorderStatus _status = new(false, "", "Bereit", 0, 0, null);
    private Process? _process;
    private AudioLoopback? _audio;
    private MicrophoneCapture? _microphone;
    private Action<Exception>? _audioFaultHandler;
    private Action<Exception>? _microphoneFaultHandler;
    private Task? _stderrTask;
    private Task? _progressTask;
    private CancellationTokenSource? _startCancellation;
    private TaskCompletionSource<bool>? _firstFrame;
    private volatile bool _intentionalStop;
    private int _disposed;
    private string? _failure;
    private string _audioWarning = "";
    private long _audioGeneration;
    private bool _audioFaulted;
    private EncoderPreferenceKey? _preferredEncoderKey;
    private Encoder? _preferredEncoder;
    private static readonly object LogLock = new();

    public Recorder() : this(DisplayCatalog.GetDisplays) { }
    internal Recorder(Func<IReadOnlyList<DisplaySource>> getDisplays)
        => _getDisplays = getDisplays ?? throw new ArgumentNullException(nameof(getDisplays));

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

    public async Task StartAsync(AppSettings settings, DisplaySource display, string sessionPrefix, CancellationToken ct = default,
        Func<CancellationToken, Task>? beforeCapture = null)
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
            _intentionalStop = false;
            long audioGeneration;
            lock (_errorLock)
            {
                _failure = null;
                _audioWarning = "";
                _audioFaulted = false;
                audioGeneration = ++_audioGeneration;
                _stderr.Clear();
            }
            Publish(new(false, "", "Hardware-Encoder wird mit dem ausgewählten Bildschirm geprüft …", 0, 0, null));
            var encoder = await SelectEncoderAsync(executable, options, display, startup.Token).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();
            if (beforeCapture is not null) await beforeCapture(startup.Token).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();
            // Probing and the asynchronous final gate can take several seconds. A hotplug
            // may reuse an output index; validate after both before opening any capture source.
            DisplayCatalog.ValidateSelection(display, _getDisplays());
            SessionPrefix = sessionPrefix;
            _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (options.AudioEnabled)
            {
                try
                {
                    _audio = new AudioLoopback(options.AudioDeviceId);
                    _audioFaultHandler = ex => AudioSourceFailed(audioGeneration, "Systemton", ex);
                    _audio.Faulted += _audioFaultHandler;
                }
                catch (Exception ex)
                {
                    _audioWarning = "Systemton nicht verfügbar: " + ex.Message;
                    throw new IOException(_audioWarning + " Das ausgewählte Ausgabegerät prüfen.", ex);
                }
            }
            if (options.MicrophoneEnabled)
            {
                try
                {
                    _microphone = new MicrophoneCapture(options.MicrophoneDeviceId);
                    _microphoneFaultHandler = ex => AudioSourceFailed(audioGeneration, "Mikrofon", ex);
                    _microphone.Faulted += _microphoneFaultHandler;
                }
                catch (Exception ex)
                {
                    _audioWarning = "Mikrofon nicht verfügbar: " + ex.Message;
                    throw new IOException(_audioWarning + " Windows-Mikrofonzugriff und ausgewähltes Gerät prüfen.", ex);
                }
            }
            // Both streams use this epoch. Audio is numbered continuously from zero, including silent gaps.
            var startedUtc = DateTime.UtcNow;
            var originMicroseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
            var captureClock = Stopwatch.StartNew();
            try { _audio?.Start(captureClock); }
            catch (Exception ex)
            {
                _audioWarning = "Systemtonaufnahme kann nicht starten: " + ex.Message;
                throw new IOException(_audioWarning, ex);
            }
            try { _microphone?.Start(captureClock); }
            catch (Exception ex)
            {
                _audioWarning = "Mikrofonaufnahme kann nicht starten: " + ex.Message;
                throw new IOException(_audioWarning + " Windows-Mikrofonzugriff prüfen.", ex);
            }
            var args = BuildArguments(options, display, encoder, _audio, sessionPrefix, originMicroseconds, false, _microphone);
            var process = new Process { StartInfo = CreateStartInfo(executable, args) };
            _process = process;
            if (!process.Start()) throw new IOException("FFmpeg konnte nicht gestartet werden.");
            LowerPriority(process);
            _stderrTask = ReadErrorsAsync(process);
            _progressTask = ReadProgressAsync(process, encoder.Name, startedUtc, _firstFrame, options.Fps);
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
        var preferenceKey = EncoderPreferenceKey.Create(executable, settings, display);
        var preferred = mode == "auto" && preferenceKey is not null && preferenceKey == _preferredEncoderKey ? _preferredEncoder : null;
        if (preferred is not null)
            candidates = [preferred, .. candidates.Where(candidate => candidate != preferred)];
        var errors = new List<string>();
        foreach (var encoder in candidates)
        {
            ct.ThrowIfCancellationRequested();
            Publish(_status with { Encoder = encoder.Name, Message = $"Prüfe {encoder.Name} …" });
            var result = await ProbeAsync(executable, BuildArguments(settings, display, encoder, null, "", 0, true), ct).ConfigureAwait(false);
            if (result.Success)
            {
                if (encoder != Software && preferenceKey is not null)
                { _preferredEncoderKey = preferenceKey; _preferredEncoder = encoder; }
                return encoder;
            }
            if (encoder == _preferredEncoder && preferenceKey == _preferredEncoderKey)
            { _preferredEncoderKey = null; _preferredEncoder = null; }
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
        AudioLoopback? audio, string prefix, long originMicroseconds, bool probe, MicrophoneCapture? microphone = null)
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
        if (microphone is not null)
        {
            args.AddRange(["-thread_queue_size", "64", "-probesize", "32", "-analyzeduration", "0",
                "-f", microphone.RawFormat, "-ar", microphone.SampleRate.ToString(CultureInfo.InvariantCulture),
                "-ac", microphone.Channels.ToString(CultureInfo.InvariantCulture), "-i", microphone.PipePath]);
        }
        var graph = $"ddagrab=output_idx={display.OutputIndex}:framerate={s.Fps}:draw_mouse={(s.CaptureCursor ? 1 : 0)}:output_fmt=bgra";
        // Align the first frame to the audio epoch, then use ddagrab's monotonic clock.
        // A later Windows clock correction must not jump the video's timestamps.
        graph += probe ? ",setpts=PTS-STARTPTS"
            : $",setpts='if(eq(N,0),(RTCTIME-{originMicroseconds})/(1000000*TB),PREV_OUTPTS+PTS-PREV_INPTS)'";
        graph += encoder.VideoFilters + "[screen]";
        var audioGraph = BuildAudioFilterGraph(audio is not null, microphone is not null, s.SystemGainPercent, s.MicrophoneGainPercent);
        if (audioGraph.Length > 0) graph += ";" + audioGraph;
        args.AddRange(["-filter_complex", graph, "-map", "[screen]"]);
        if (audioGraph.Length > 0)
            args.AddRange(["-map", "[sound]", "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-ar", "48000"]);
        else args.Add("-an");
        args.AddRange(["-c:v", encoder.Codec]);
        args.AddRange(BuildEncoderOptions(encoder.Codec, s.EncodingQuality));
        var bitrate = RecordingQuality.ResolveBitrateKbps(s, display);
        var peakBitrate = RecordingQuality.PeakBitrateKbps(bitrate);
        args.AddRange(["-profile:v", "high", "-b:v", $"{bitrate}k", "-maxrate", $"{peakBitrate}k", "-bufsize", $"{peakBitrate * 2}k",
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

    private static string[] BuildEncoderOptions(string codec, string quality)
    {
        var mode = RecordingQuality.NormalizeQuality(quality);
        return codec switch
        {
            // Single pass and no lookahead keep the default hardware path inexpensive.
            // Moving beyond the fastest preset preserves detail without downloading GPU frames.
            "h264_nvenc" => ["-preset", mode switch { "Efficiency" => "p2", "Quality" => "p5", _ => "p4" },
                "-tune", mode == "Efficiency" ? "ll" : "hq", "-rc", "vbr", "-multipass", "disabled", "-rc-lookahead", "0"],
            "h264_amf" => ["-usage", "transcoding", "-quality", mode switch { "Efficiency" => "speed", "Quality" => "quality", _ => "balanced" },
                "-rc", "vbr_peak", "-preanalysis", "0"],
            "h264_qsv" => ["-preset", mode switch { "Efficiency" => "veryfast", "Quality" => "fast", _ => "faster" },
                "-look_ahead", "0"],
            "libx264" => ["-preset", mode switch { "Efficiency" => "ultrafast", "Quality" => "veryfast", _ => "superfast" },
                "-tune", "zerolatency", "-threads", "2"],
            _ => throw new ArgumentException("Unbekannter Video-Encoder.", nameof(codec))
        };
    }

    private static string BuildAudioFilterGraph(bool systemAudio, bool microphone, int systemGain, int microphoneGain)
    {
        if (systemGain is < 0 or > 200 || microphoneGain is < 0 or > 200)
            throw new ArgumentOutOfRangeException(nameof(systemGain), "Audiolautstärke muss zwischen 0 und 200 % liegen.");
        if (!systemAudio && !microphone) return "";
        var graph = new List<string>();
        var inputs = new List<string>();
        void AddSource(int input, int gain, string label)
        {
            // Both bounded pipes start at the same monotonic epoch. Drift correction handles
            // differing hardware rates; stereo and sample-rate conversion happen before mixing.
            graph.Add($"[{input}:a:0]aresample=48000:async=1:first_pts=0,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={(gain / 100.0).ToString("0.##", CultureInfo.InvariantCulture)}[{label}]");
            inputs.Add("[" + label + "]");
        }
        if (systemAudio) AddSource(0, systemGain, "systemaudio");
        if (microphone) AddSource(systemAudio ? 1 : 0, microphoneGain, "microphoneaudio");
        var joined = string.Concat(inputs);
        // Disabling normalization preserves game volume when the microphone is silent.
        // The limiter prevents clipping without automatically boosting quiet recordings.
        var mix = inputs.Count == 2 ? "amix=inputs=2:duration=longest:dropout_transition=0:normalize=0," : "";
        graph.Add(joined + mix + "alimiter=limit=0.95:level=false:latency=true[sound]");
        return string.Join(';', graph);
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

    private async Task ReadProgressAsync(Process process, string encoder, DateTime startedUtc, TaskCompletionSource<bool> firstFrame, int targetFps)
    {
        long frames = 0;
        double fps = 0;
        var recentFps = new RecentFrameRate(targetFps);
        var videoElapsed = TimeSpan.Zero;
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
                else if (key == "out_time_us" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                    videoElapsed = TimeSpan.FromTicks(Math.Clamp(microseconds, 0, TimeSpan.MaxValue.Ticks / 10) * 10);
                else if (key == "progress")
                {
                    if (ReferenceEquals(_process, process))
                    {
                        if (frames > 0) firstFrame.TrySetResult(true);
                        var deliveredFps = recentFps.Update(frames, Stopwatch.GetTimestamp(), fps);
                        Publish(new(!_intentionalStop && frames > 0, encoder, _intentionalStop ? "Aufnahme wird beendet …" : "Aufnahme läuft", deliveredFps, frames, startedUtc, VideoElapsed: videoElapsed));
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

    private void AudioSourceFailed(long generation, string source, Exception ex)
    {
        if (_intentionalStop || IsExited(_process)) return;
        string failure;
        TaskCompletionSource<bool>? firstFrame;
        lock (_errorLock)
        {
            // A disconnected headset can fault loopback and microphone together. Handle one
            // fault per session; an old callback must never stop a newly started recording.
            if (generation != _audioGeneration || _audioFaulted) return;
            _audioFaulted = true;
            _audioWarning = source + " ausgefallen: " + ex.Message;
            _failure = "Audioaufnahme fehlgeschlagen: " + _audioWarning;
            failure = _failure;
            firstFrame = _firstFrame;
        }
        WriteLog(failure);
        firstFrame?.TrySetException(new IOException(failure, ex));
        _ = StopAfterFailureAsync(generation);
    }

    private async Task StopAfterFailureAsync(long generation)
    {
        try
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (generation != _audioGeneration) return;
                _intentionalStop = true;
                await StopResourcesAsync().ConfigureAwait(false);
                SessionPrefix = null;
                Publish(_status with { Recording = false, Fps = 0, Message = _failure ?? "Audioaufnahme beendet" });
            }
            finally { _lifecycle.Release(); }
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
            audio.Faulted -= _audioFaultHandler;
            try { await audio.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { WriteLog("System audio shutdown: " + ex.Message); }
            finally
            {
                try { audio.Dispose(); }
                catch (Exception ex) { WriteLog("System audio disposal: " + ex.Message); }
            }
        }
        _audioFaultHandler = null;
        var microphone = _microphone;
        _microphone = null;
        if (microphone is not null)
        {
            microphone.Faulted -= _microphoneFaultHandler;
            try { await microphone.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { WriteLog("Microphone shutdown: " + ex.Message); }
            finally
            {
                try { microphone.Dispose(); }
                catch (Exception ex) { WriteLog("Microphone disposal: " + ex.Message); }
            }
        }
        _microphoneFaultHandler = null;
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
        if (_audio is { } audio)
            status = status with { AudioPeak = status.Recording ? audio.PeakLevel : 0, AudioDevice = audio.DeviceName };
        if (_microphone is { } microphone)
            status = status with { MicrophonePeak = status.Recording ? microphone.PeakLevel : 0, MicrophoneDevice = microphone.DeviceName };
        status = status with { AudioWarning = _audioWarning };
        _status = status;
        try { Changed?.Invoke(status); } catch (Exception ex) { WriteLog("Status observer: " + ex.Message); }
    }
    private static void Validate(AppSettings s, DisplaySource display, string prefix)
    {
        if (s.Fps is < 1 or > 60 || s.VideoBitrateKbps is < 500 or > RecordingQuality.MaximumBitrateKbps || s.SegmentMinutes is < 1 or > 60)
            throw new ArgumentException("Ungültige Bildrate, Bitrate oder Segmentlänge.");
        _ = RecordingQuality.NormalizeQuality(s.EncodingQuality);
        if (s.SystemGainPercent is < 0 or > 200 || s.MicrophoneGainPercent is < 0 or > 200)
            throw new ArgumentException("Audiolautstärke muss zwischen 0 und 200 % liegen.");
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
