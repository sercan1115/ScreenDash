using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ScreenDash;

var directory = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("work", "segments"));
var shortTest = args.Length > 1 && args[1] == "short";
var testStartedUtc = DateTime.UtcNow;
Directory.CreateDirectory(directory);
var result = new Dictionary<string, object?>();
using var recorder = new Recorder();
try
{
    var displays = DisplayCatalog.GetDisplays();
    if (displays.Count < 2) throw new InvalidOperationException("This integration check needs two monitors.");
    var display = displays[1];
    result["display"] = display;
    var prefix = "screendash_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'") + "_" + Guid.NewGuid().ToString("N")[..8];
    var settings = new AppSettings { RecordingDirectory = directory, AudioEnabled = shortTest, SegmentMinutes = 1, Fps = 15 };
    recorder.Changed += status => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {status.Recording} {status.Encoder} frames={status.Frames} {status.Message}");
    using var startDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await recorder.StartAsync(settings, display, prefix, startDeadline.Token);
    result["encoder"] = recorder.Status.Encoder;
    var watch = Stopwatch.StartNew();
    using var self = Process.GetCurrentProcess();
    using var ffmpegProcess = Process.GetProcessById(recorder.ProcessId!.Value);
    var cpuBefore = self.TotalProcessorTime + ffmpegProcess.TotalProcessorTime;
    await Task.Delay(TimeSpan.FromSeconds(shortTest ? 8 : 64));
    self.Refresh(); ffmpegProcess.Refresh();
    result["seconds"] = watch.Elapsed.TotalSeconds;
    result["cpuPercentOfMachine"] = ((self.TotalProcessorTime + ffmpegProcess.TotalProcessorTime) - cpuBefore).TotalSeconds / watch.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
    result["workingSetMiB"] = (self.WorkingSet64 + ffmpegProcess.WorkingSet64) / 1048576.0;
    if (!recorder.Status.Recording) throw new InvalidOperationException("Capture stopped during the test: " + recorder.Status.Message);
    result["statusBeforeStop"] = recorder.Status;
    await recorder.StopAsync();
    if (recorder.ProcessId is not null || recorder.Status.Recording) throw new InvalidOperationException("The recording process did not stop.");
    var files = Directory.GetFiles(directory, prefix + "_*.mkv").OrderBy(f => f).ToArray();
    if (files.Length != (shortTest ? 1 : 2)) throw new InvalidOperationException($"Unexpected number of segments: {files.Length}.");
    var ffprobe = Path.Combine(Path.GetDirectoryName(Recorder.FindFfmpeg())!, "ffprobe.exe");
    var segments = new List<object>();
    foreach (var file in files)
    {
        var json = await ExecuteAsync(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", file]);
        using var data = JsonDocument.Parse(json);
        var streams = data.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (streams.Length != (shortTest ? 2 : 1) || streams[0].GetProperty("codec_type").GetString() != "video")
            throw new InvalidOperationException("Capture contains an unexpected stream.");
        if (shortTest && (streams[1].GetProperty("codec_type").GetString() != "audio" || streams[1].GetProperty("codec_name").GetString() != "aac"))
            throw new InvalidOperationException("AAC audio is missing.");
        if (streams[0].GetProperty("width").GetInt32() != display.Width || streams[0].GetProperty("height").GetInt32() != display.Height)
            throw new InvalidOperationException("Capture dimensions do not match the second monitor.");
        var packets = await ExecuteAsync(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_packets", "-show_entries", "packet=pts_time,dts_time", "-of", "csv=p=0", file]);
        double lastPts = -1, lastDts = -1;
        var count = 0;
        foreach (var line in packets.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var timestamps = line.Trim().Split(',');
            var pts = double.Parse(timestamps[0], CultureInfo.InvariantCulture);
            var dts = double.Parse(timestamps[1], CultureInfo.InvariantCulture);
            if (pts <= lastPts || dts <= lastDts) throw new InvalidOperationException("A segment has non-monotonic packet timestamps.");
            lastPts = pts; lastDts = dts; count++;
        }
        var duration = double.Parse(data.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var actualFps = count / duration;
        if (actualFps < 13 || actualFps > 17) throw new InvalidOperationException($"Unexpected frame rate: {actualFps:F3}.");
        segments.Add(new { file, bytes = new FileInfo(file).Length, duration, videoPackets = count, actualFps, strictlyIncreasingTimestamps = true });
    }
    if (shortTest)
    {
        var export = Path.Combine(directory, prefix + "-export.mp4");
        await Recorder.ExportAsync(files[0], export);
        using var exportData = JsonDocument.Parse(await ExecuteAsync(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", export]));
        if (exportData.RootElement.GetProperty("streams").GetArrayLength() != 2) throw new InvalidOperationException("MP4 export has missing streams.");
        result["export"] = export;
    }
    var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenDash", "logs", "ffmpeg.log");
    var warnings = File.Exists(log) ? File.ReadLines(log).Where(line => line.Contains("Non-monotonic", StringComparison.OrdinalIgnoreCase)
        && line.Length > 28 && DateTime.TryParse(line[..28], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when) && when >= testStartedUtc).ToArray() : [];
    if (warnings.Length > 0) throw new InvalidOperationException("Encoder logged non-monotonic timestamps: " + string.Join(" | ", warnings));
    result["nonMonotonicTimestampWarnings"] = warnings.Length;
    result["segments"] = segments;
    result["success"] = true;
    Console.WriteLine(shortTest ? "PASS: second monitor, AAC audio, MP4 export, increasing timestamps without encoder warnings, clean stop."
        : "PASS: second monitor, audio off, two playable segments, increasing timestamps, clean stop.");
    return 0;
}
catch (Exception ex)
{
    result["success"] = false;
    result["error"] = ex.ToString();
    Console.Error.WriteLine(ex);
    return 1;
}
finally
{
    await recorder.StopAsync();
    File.WriteAllText(Path.Combine(directory, "segment-test-result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task<string> ExecuteAsync(string executable, string[] arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("Could not launch ffprobe.");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new IOException(await error);
    return await output;
}
