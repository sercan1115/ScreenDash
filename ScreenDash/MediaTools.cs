using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ScreenDash;

/// <summary>On-demand media inspection and lossless, keyframe-aligned clip exports.</summary>
public static class MediaTools
{
    private static readonly TimeSpan MaximumMediaDuration = TimeSpan.FromDays(365);

    public static async Task<MediaDetails> ReadDetailsAsync(string source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        source = ValidateSource(source);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        ProcessResult result;
        try
        {
            result = await RunAsync(FindFfprobe(),
                ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height,duration", "-of", "json", source],
                32 * 1024, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException("Die Aufnahmeprüfung hat länger als 10 Sekunden gedauert. Bitte später erneut versuchen.");
        }
        ct.ThrowIfCancellationRequested();
        if (result.ExitCode != 0) throw new IOException("Aufnahme konnte nicht gelesen werden: " + result.Error);
        if (result.OutputTruncated) throw new IOException("FFprobe lieferte zu viele Metadaten für diese Aufnahme.");
        try
        {
            using var json = JsonDocument.Parse(result.Output);
            var root = json.RootElement;
            var streams = root.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.FirstOrDefault(s => s.TryGetProperty("codec_type", out var kind) && kind.GetString() == "video");
            if (video.ValueKind != JsonValueKind.Object) throw new IOException("Die Datei enthält keine Videospur.");
            var width = video.GetProperty("width").GetInt32();
            var height = video.GetProperty("height").GetInt32();
            if (width is < 1 or > 65535 || height is < 1 or > 65535) throw new IOException("Die Auflösung der Aufnahme ist ungültig.");
            var seconds = root.TryGetProperty("format", out var format) ? ReadSeconds(format) : null;
            seconds ??= ReadSeconds(video);
            if (seconds is not { } duration) throw new IOException("Die Dauer der Aufnahme konnte nicht ermittelt werden. Bitte die Aufnahme zuerst beenden.");
            var audio = streams.Any(s => s.TryGetProperty("codec_type", out var kind) && kind.GetString() == "audio");
            return new MediaDetails(TimeSpan.FromSeconds(duration), width, height, audio);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new IOException("FFprobe lieferte ungültige Aufnahmedaten.", ex);
        }
    }

    /// <summary>Stream copy preserves image/audio quality; the clip starts at a nearby preceding keyframe.</summary>
    public static async Task ExportClipAsync(string source, string destination, ClipRange range, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(range);
        if (range.Start < TimeSpan.Zero || range.Duration <= TimeSpan.Zero
            || range.Start > MaximumMediaDuration || range.Duration > MaximumMediaDuration
            || range.Start.Ticks > MaximumMediaDuration.Ticks - range.Duration.Ticks)
            throw new ArgumentOutOfRangeException(nameof(range), "Bitte einen gültigen Beginn und eine positive Clipdauer wählen.");
        source = ValidateSource(source);
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Für den Clip bitte eine .mp4-Datei wählen.", nameof(destination));
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Quelle und Ziel müssen verschieden sein.", nameof(destination));
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Die Zieldatei existiert bereits. Bitte einen neuen Namen wählen.");
        // Resolve the dependency before creating anything in the destination folder.
        var executable = Recorder.FindFfmpeg();
        ct.ThrowIfCancellationRequested();
        var folder = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, ".screendash-clip-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var result = await RunAsync(executable,
                ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-ss", Seconds(range.Start), "-i", source,
                    "-t", Seconds(range.Duration), "-map", "0:v:0", "-map", "0:a?", "-c", "copy",
                    "-avoid_negative_ts", "make_zero", "-movflags", "+faststart", temporary],
                4000, ct).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new IOException("Clip-Export fehlgeschlagen: " + result.Error);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new IOException("Der Clip enthält keine Daten. Bitte einen früheren Beginn wählen.");
            // File.Move without overwrite also protects a target created while FFmpeg was running.
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ValidateSource(string source)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(source)) throw new FileNotFoundException("Die Aufnahme wurde nicht gefunden.", source);
        return source;
    }

    private static double? ReadSeconds(JsonElement element)
    {
        if (!element.TryGetProperty("duration", out var value)) return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds > 0 && seconds <= MaximumMediaDuration.TotalSeconds ? seconds : null;
    }

    private static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.#######", CultureInfo.InvariantCulture);

    private static string FindFfprobe()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "ffprobe.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffprobe.exe")
        };
        try { candidates.Add(Path.Combine(Path.GetDirectoryName(Recorder.FindFfmpeg())!, "ffprobe.exe")); }
        catch (FileNotFoundException) { }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try { candidates.Add(Path.Combine(directory.Trim().Trim('"'), "ffprobe.exe")); }
            catch (ArgumentException) { }
        }
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        throw new FileNotFoundException("FFprobe fehlt. Bitte ffprobe.exe aus dem FFmpeg-Paket neben ffmpeg.exe legen oder FFmpeg vollständig installieren.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error, bool OutputTruncated);
    private sealed record CapturedText(string Text, bool Truncated);

    private static async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, int outputLimit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Das Medienwerkzeug konnte nicht gestartet werden.");
        ChildProcessGuard.Attach(process);
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        process.StandardInput.Close();
        var output = ReadBoundedAsync(process.StandardOutput, outputLimit, false);
        var errors = ReadBoundedAsync(process.StandardError, 4000, true);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            // An inherited pipe must not hold completion open indefinitely.
            await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            var captured = await output.ConfigureAwait(false);
            var error = (await errors.ConfigureAwait(false)).Text.Trim();
            return new ProcessResult(process.ExitCode, captured.Text, error.Length == 0 ? $"Exit-Code {process.ExitCode}" : error, captured.Truncated);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            ct.ThrowIfCancellationRequested();
            if (ex is TimeoutException) throw new IOException("Das Medienwerkzeug hat seine Ausgabe nicht rechtzeitig geschlossen.", ex);
            throw;
        }
    }

    private static async Task<CapturedText> ReadBoundedAsync(StreamReader reader, int limit, bool keepTail)
    {
        var captured = new StringBuilder();
        var buffer = new char[1024];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (keepTail)
            {
                captured.Append(buffer, 0, count);
                if (captured.Length > limit) { truncated = true; captured.Remove(0, captured.Length - limit); }
            }
            else
            {
                var remaining = Math.Max(0, limit - captured.Length);
                captured.Append(buffer, 0, Math.Min(count, remaining));
                if (count > remaining) truncated = true;
            }
        }
        return new CapturedText(captured.ToString(), truncated);
    }
}
