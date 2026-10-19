using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScreenDash;

// Command checks never start a recorder. The optional integration check encodes only
// a generated testsrc2 pattern and requires an explicit path to a real FFmpeg executable.
internal static class QualityChecks
{
    private static int _checks;
    private static readonly DisplaySource Display = new("synthetic", "Synthetic 1080p", 0, 0, 0, 0, 1920, 1080);

    internal static int RunArgumentChecks()
    {
        _checks = 0;
        var balanced = new AppSettings { Fps = 30, EncodingQuality = "Balanced", AutoVideoBitrate = true };
        var before = balanced.Clone();
        var target = RecordingQuality.ResolveBitrateKbps(balanced, Display);
        Check(balanced.Fps == before.Fps && balanced.VideoBitrateKbps == before.VideoBitrateKbps
            && balanced.AutoVideoBitrate == before.AutoVideoBitrate && balanced.EncodingQuality == before.EncodingQuality,
            "Bitrate calculation does not mutate the settings snapshot");
        Check(target > 3_000 && RecordingQuality.PeakBitrateKbps(target) > target,
            "Balanced 1080p capture has room for fine detail and bounded motion bursts");
        Check(RecordingQuality.RecommendedBitrateKbps(1280, 720, 30) < target
            && target < RecordingQuality.RecommendedBitrateKbps(3840, 2160, 30),
            "Automatic bitrate follows the selected monitor's native size");
        Check(RecordingQuality.RecommendedBitrateKbps(1920, 1080, 15) < target
            && target < RecordingQuality.RecommendedBitrateKbps(1920, 1080, 60),
            "Automatic bitrate increases with motion sampling instead of keeping a fixed 3 Mbps");
        Check(RecordingQuality.RecommendedBitrateKbps(1920, 1080, 30, "Efficiency") < target
            && target < RecordingQuality.RecommendedBitrateKbps(1920, 1080, 30, "Quality"),
            "Quality choices expose the storage and detail tradeoff");
        Check(RecordingQuality.RecommendedBitrateKbps(int.MaxValue, int.MaxValue, 60, "Quality") == RecordingQuality.MaximumBitrateKbps
            && RecordingQuality.PeakBitrateKbps(RecordingQuality.MaximumBitrateKbps) == RecordingQuality.MaximumBitrateKbps,
            "Huge capture sizes and VBR peaks remain within the 100 Mbps ceiling");
        var quality1080 = RecordingQuality.RecommendedBitrateKbps(1920, 1080, 60, "Quality");
        var quality1440 = RecordingQuality.RecommendedBitrateKbps(2560, 1440, 60, "Quality");
        var quality4K = RecordingQuality.RecommendedBitrateKbps(3840, 2160, 60, "Quality");
        Check(quality1080 >= 30_000 && quality1440 > quality1080 && quality4K > quality1440,
            "High quality 60 FPS capture reserves progressively more detail for 1080p, 1440p and 4K");
        Check(RecordingQuality.PeakBitrateKbps(quality1080) > quality1080
            && RecordingQuality.PeakBitrateKbps(quality1440) > quality1440
            && RecordingQuality.PeakBitrateKbps(quality4K) <= RecordingQuality.MaximumBitrateKbps,
            "High quality motion bursts retain headroom at 1080p and 1440p without exceeding the 4K ceiling");
        Check(RecordingQuality.RecommendedBitrateKbps(1, 1, 5, "Efficiency") >= 500,
            "Tiny valid inputs still receive a usable bounded bitrate");
        foreach (var (width, height, fps) in new[] { (0, 1080, 30), (1920, -1, 30), (1920, 1080, 0), (1920, 1080, 61) })
            ExpectArgumentFailure(() => RecordingQuality.RecommendedBitrateKbps(width, height, fps), "Invalid capture geometry or frame rate is rejected");
        ExpectArgumentFailure(() => RecordingQuality.NormalizeQuality("Unlimited"), "Unknown quality modes are rejected");

        balanced.AutoVideoBitrate = false;
        balanced.VideoBitrateKbps = 3_000;
        Check(RecordingQuality.ResolveBitrateKbps(balanced, Display with { Width = 3840, Height = 2160 }) == 3_000,
            "A manually selected or migrated bitrate stays unchanged on a larger monitor");
        balanced.VideoBitrateKbps = int.MaxValue;
        Check(RecordingQuality.ResolveBitrateKbps(balanced, Display) == RecordingQuality.MaximumBitrateKbps,
            "External oversized manual bitrates cannot bypass the encoder and disk budget");
        foreach (var encoder in new[] { "Nvenc", "Amf", "Qsv", "Software" })
        foreach (var quality in new[] { "Efficiency", "Balanced", "Quality" })
        {
            var settings = new AppSettings { Fps = 30, EncodingQuality = quality, AutoVideoBitrate = true };
            var probe = Arguments(settings, Display, encoder, true);
            var final = Arguments(settings, Display, encoder, false);
            Check(VideoOptions(probe).SequenceEqual(VideoOptions(final)),
                $"{encoder}/{quality}: the hardware probe validates the same encoder and bitrate options as the recording");
            var rate = ParseKbps(Value(final, "-b:v"));
            var peak = ParseKbps(Value(final, "-maxrate"));
            Check(rate == RecordingQuality.ResolveBitrateKbps(settings, Display) && peak >= rate && peak <= RecordingQuality.MaximumBitrateKbps
                && ParseKbps(Value(final, "-bufsize")) == peak * 2,
                $"{encoder}/{quality}: rate control allows detail bursts with a bounded VBV buffer");
            Check(Value(final, "-profile:v") == "high" && Value(final, "-bf") == "0"
                && Value(final, "-g") == "60" && Value(final, "-fps_mode") == "passthrough"
                && Value(final, "-filter_complex").Contains("framerate=30", StringComparison.Ordinal),
                $"{encoder}/{quality}: native capture cadence and monotonic low-delay timestamp policy stay intact");
            if (encoder != "Software")
                Check(!Value(final, "-filter_complex").Contains("hwdownload", StringComparison.Ordinal)
                    && Value(final, "-c:v") != "libx264",
                    $"{encoder}/{quality}: hardware capture never adds a CPU frame download or software encoder");
        }
        Check(Value(Arguments(new AppSettings { EncodingQuality = "Balanced" }, Display, "Nvenc", false), "-preset") == "p4"
            && Value(Arguments(new AppSettings { EncodingQuality = "Balanced" }, Display, "Amf", false), "-quality") == "balanced"
            && Value(Arguments(new AppSettings { EncodingQuality = "Balanced" }, Display, "Qsv", false), "-preset") == "faster",
            "Balanced mode no longer uses each GPU encoder's lowest quality speed preset");
        Check(Value(Arguments(new AppSettings { EncodingQuality = "Quality" }, Display, "Software", false), "-threads") == "2",
            "Explicit CPU encoding keeps its two-thread bound even in high quality mode");
        foreach (var encoder in new[] { "Nvenc", "Amf", "Qsv" })
        {
            var high = Arguments(new AppSettings { Fps = 60, EncodingQuality = "Quality", AutoVideoBitrate = true }, Display, encoder, false);
            Check(ParseKbps(Value(high, "-b:v")) == quality1080 && Value(high, "-g") == "120"
                && Value(high, "-filter_complex").Contains("framerate=60", StringComparison.Ordinal)
                && !Value(high, "-filter_complex").Contains("scale", StringComparison.Ordinal),
                $"{encoder}: high quality 60 FPS preserves native geometry and does not replace frames with upscaling");
        }
        Check(Arguments(new AppSettings { AudioEnabled = false }, Display, "Nvenc", false).Contains("-an"),
            "Quality settings do not add or initialize audio sources");
        return _checks;
    }

    internal static async Task<int> RunIntegrationAsync(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException("A real FFmpeg executable is required.", executable);
        var scratch = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ScreenDash-quality-checks-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(scratch);
        try
        {
            var legacy = Path.Combine(scratch, "legacy.mkv");
            var previous = Path.Combine(scratch, "previous-10mbps.mkv");
            var improved = Path.Combine(scratch, "quality.mkv");
            const string source = "testsrc2=size=1920x1080:rate=60:duration=2";
            const int sourceFrames = 120;
            foreach (var (path, bitrate) in new[] { (legacy, 3_000), (previous, 10_000) })
                await ExecuteAsync(executable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", source,
                    "-an", "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency", "-threads", "2",
                    "-b:v", $"{bitrate}k", "-maxrate", $"{bitrate}k", "-bufsize", $"{bitrate * 2}k", "-g", "120", "-bf", "0", "-f", "matroska", path]);
            var settings = new AppSettings { Fps = 60, EncodingQuality = "Quality", AutoVideoBitrate = true, AudioEnabled = false };
            var recording = Arguments(settings, Display, "Software", false);
            var options = VideoOptions(recording);
            await ExecuteAsync(executable, ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", source,
                "-an", .. options, "-f", "matroska", improved]);

            var legacySimilarity = await SimilarityAsync(executable, legacy, source, sourceFrames);
            var previousSimilarity = await SimilarityAsync(executable, previous, source, sourceFrames);
            var improvedSimilarity = await SimilarityAsync(executable, improved, source, sourceFrames);
            if (improvedSimilarity <= legacySimilarity + 0.001 || improvedSimilarity <= previousSimilarity + 0.001)
                throw new IOException($"Generated motion pattern did not improve: 3 Mbps SSIM {legacySimilarity:F6}, 10 Mbps {previousSimilarity:F6}, high quality {improvedSimilarity:F6}.");
            var ffprobe = Path.Combine(Path.GetDirectoryName(executable)!, "ffprobe.exe");
            using var details = JsonDocument.Parse(await ExecuteAsync(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", improved]));
            var streams = details.RootElement.GetProperty("streams");
            var stream = streams[0];
            if (streams.GetArrayLength() != 1 || stream.GetProperty("codec_name").GetString() != "h264"
                || stream.GetProperty("width").GetInt32() != 1920 || stream.GetProperty("height").GetInt32() != 1080
                || stream.GetProperty("profile").GetString() != "High" || stream.GetProperty("pix_fmt").GetString() != "yuv420p")
                throw new IOException("Generated video dimensions, H.264 High profile or compatible pixel format are incorrect.");
            var packets = await ExecuteAsync(ffprobe, ["-v", "error", "-select_streams", "v:0", "-show_packets", "-show_entries", "packet=pts_time,dts_time", "-of", "csv=p=0", improved]);
            double previousPts = -1, previousDts = -1;
            var frames = 0;
            foreach (var packet in packets.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var columns = packet.Trim().Split(',');
                var pts = double.Parse(columns[0], CultureInfo.InvariantCulture);
                var dts = double.Parse(columns[1], CultureInfo.InvariantCulture);
                if (pts <= previousPts || dts <= previousDts) throw new IOException("Generated video has non-monotonic timestamps.");
                previousPts = pts; previousDts = dts; frames++;
            }
            if (frames != sourceFrames) throw new IOException("Generated video did not preserve all 120 source frames.");
            Console.WriteLine($"PASS: synthetic 1080p60 H.264 High pattern, 3 Mbps SSIM={legacySimilarity:F6}, 10 Mbps SSIM={previousSimilarity:F6}, high quality ({RecordingQuality.ResolveBitrateKbps(settings, Display)} kbps) SSIM={improvedSimilarity:F6}; 120 increasing timestamps, no audio or screen capture. Synthetic CPU encoding checks fidelity only; GPU load and real games require hardware testing.");
            return 0;
        }
        finally { Directory.Delete(scratch, true); }
    }

    private static async Task<double> SimilarityAsync(string executable, string video, string source, int frames)
    {
        var result = await ExecuteAsync(executable, ["-hide_banner", "-loglevel", "info", "-nostdin", "-i", video,
            "-f", "lavfi", "-i", source, "-lavfi", "[0:v][1:v]ssim", "-frames:v", frames.ToString(CultureInfo.InvariantCulture), "-an", "-f", "null", "-"]);
        var matches = Regex.Matches(result, @"All:([0-9.]+)");
        if (matches.Count == 0) throw new IOException("FFmpeg did not report synthetic SSIM.");
        return double.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ExecuteAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Synthetic encoder check could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { process.Kill(true); await process.WaitForExitAsync(); throw; }
        var result = await output + await errors;
        if (process.ExitCode != 0) throw new IOException(result);
        return result;
    }

    private static List<string> Arguments(AppSettings settings, DisplaySource display, string encoderName, bool probe)
    {
        var encoder = typeof(Recorder).GetField(encoderName, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        return (List<string>)typeof(Recorder).GetMethod("BuildArguments", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [settings, display, encoder, null, "synthetic_quality", 0L, probe, null])!;
    }

    private static string[] VideoOptions(List<string> arguments)
    {
        var first = arguments.IndexOf("-c:v");
        var last = arguments.IndexOf("-fps_mode") + 2;
        return arguments.Skip(first).Take(last - first).ToArray();
    }
    private static string Value(List<string> arguments, string name) => arguments[arguments.IndexOf(name) + 1];
    private static int ParseKbps(string value) => int.Parse(value.TrimEnd('k'), CultureInfo.InvariantCulture);
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
    }
    private static void ExpectArgumentFailure(Action action, string description)
    {
        try { action(); throw new InvalidOperationException(description); }
        catch (ArgumentException) { _checks++; }
    }
}
