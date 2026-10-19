using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ScreenDash;

// Process checks use this console application's apphost as a fake FFmpeg.
// They never initialize WASAPI, Desktop Duplication, or an actual encoder.
internal static class ProcessRegression
{
    internal const string FakeLogVariable = "SCREENDASH_TEST_FAKE_FFMPEG_LOG";
    private static int _checks;

    public static async Task<int> RunAsync()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "ScreenDash-process-checks-" + Guid.NewGuid().ToString("N"));
        var apphost = Path.Combine(AppContext.BaseDirectory, "ScreenDash.RecorderTests.exe");
        var fake = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        var fakeProbe = Path.Combine(AppContext.BaseDirectory, "ffprobe.exe");
        var previousLog = Environment.GetEnvironmentVariable(FakeLogVariable);
        var createdFake = false;
        var createdFakeProbe = false;
        Directory.CreateDirectory(scratch);
        try
        {
            Check(File.Exists(apphost), "Test apphost is available");
            _checks += QualityChecks.RunArgumentChecks();
            if (File.Exists(fake)) throw new IOException("Process checks require an isolated test output directory without ffmpeg.exe.");
            File.Copy(apphost, fake, false);
            createdFake = true;
            if (File.Exists(fakeProbe)) throw new IOException("Process checks require an isolated test output directory without ffprobe.exe.");
            File.Copy(apphost, fakeProbe, false);
            createdFakeProbe = true;
            var log = Path.Combine(scratch, "child-pid.txt");
            Environment.SetEnvironmentVariable(FakeLogVariable, log);

            var display = new DisplaySource("0:0:DISPLAY1", "Monitor", 0, 0, 0, 0, 1920, 1080);
            ValidateSnapshot(display, [display with { Name = "A harmless label change" }]);
            Check(true, "Display label changes preserve the selected desktop");
            ExpectDisplayFailure(display, []);
            ExpectDisplayFailure(display, [display with { Id = "0:0:DISPLAY2" }]);
            ExpectDisplayFailure(display, [display with { AdapterIndex = 1 }]);
            ExpectDisplayFailure(display, [display with { OutputIndex = 1 }]);
            ExpectDisplayFailure(display, [display with { Width = 2560 }]);
            ExpectDisplayFailure(display, [display with { X = 1920 }]);

            CheckRecentFrameRate();
            CheckEncoderPreferenceKeys(scratch, display);
            await CheckEncoderPreferenceAsync(fake, display, log);
            await CheckBeforeCaptureAsync(scratch, display, log);
            await CheckProgressAsync(apphost, log);

            var flood = await ProbeAsync(apphost, ["--fake-ffmpeg", "flood"], CancellationToken.None);
            Check(!flood.Success && flood.Error.EndsWith("TAIL", StringComparison.Ordinal) && flood.Error.Length <= 4000,
                "Encoder probe drains both streams and retains only the bounded error tail");
            using (var cancellation = new CancellationTokenSource())
            {
                var blockedProbe = ProbeAsync(apphost, ["--fake-ffmpeg", "wait", log], cancellation.Token);
                var pid = await WaitForPidAsync(log);
                var started = Stopwatch.StartNew();
                cancellation.Cancel();
                await ExpectCancellationAsync(blockedProbe);
                Check(started.Elapsed < TimeSpan.FromSeconds(4) && !IsRunning(pid), "Cancelling the hardware probe terminates its child promptly");
                File.Delete(log);
            }

            var source = Path.Combine(scratch, "source.mkv");
            File.WriteAllText(source, "success");
            var existing = Path.Combine(scratch, "existing.mp4");
            File.WriteAllText(existing, "existing destination");
            await ExpectIOExceptionAsync(Recorder.ExportAsync(source, existing));
            Check(File.ReadAllText(existing) == "existing destination" && File.ReadAllText(source) == "success",
                "An existing export destination and its source remain intact");

            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                var neverCreated = Path.Combine(scratch, "never-created", "cancelled.mp4");
                await ExpectCancellationAsync(Recorder.ExportAsync(source, neverCreated, cancelled.Token));
                Check(!Directory.Exists(Path.GetDirectoryName(neverCreated)) && !File.Exists(log),
                    "A pre-cancelled export creates no folder or process");
            }

            File.WriteAllText(source, "wait");
            var aborted = Path.Combine(scratch, "aborted.mp4");
            using (var cancellation = new CancellationTokenSource())
            {
                var export = Recorder.ExportAsync(source, aborted, cancellation.Token);
                var pid = await WaitForPidAsync(log);
                cancellation.Cancel();
                await ExpectCancellationAsync(export);
                Check(!IsRunning(pid), "Cancelling an export leaves no FFmpeg child");
                Check(!File.Exists(aborted) && !Directory.EnumerateFiles(scratch, ".screendash-export-*").Any()
                    && File.ReadAllText(source) == "wait", "Cancelled export removes its temporary output and preserves its source");
                File.Delete(log);
            }

            File.WriteAllText(source, "fail");
            var failed = Path.Combine(scratch, "failed.mp4");
            await ExpectIOExceptionAsync(Recorder.ExportAsync(source, failed));
            Check(!File.Exists(failed) && !Directory.EnumerateFiles(scratch, ".screendash-export-*").Any()
                && File.ReadAllText(source) == "fail", "Failed export removes its partial output and preserves its source");
            File.Delete(log);

            File.WriteAllText(source, "success");
            var completed = Path.Combine(scratch, "completed.mp4");
            await Recorder.ExportAsync(source, completed);
            Check(File.ReadAllText(completed) == "complete fake MP4" && File.ReadAllText(source) == "success"
                && !Directory.EnumerateFiles(scratch, ".screendash-export-*").Any(), "Successful export publishes its final output and keeps the source");
            File.Delete(log);

            await CheckMediaToolsAsync(scratch, source, log);

            using (var recorder = new Recorder())
            using (var stubborn = StartFake(apphost, "wait", log))
            {
                var pid = await WaitForPidAsync(log);
                typeof(Recorder).GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(recorder, stubborn);
                var started = Stopwatch.StartNew();
                await recorder.StopAsync();
                Check(started.Elapsed < TimeSpan.FromSeconds(12) && !IsRunning(pid) && recorder.ProcessId is null,
                    "Shutdown bounds the wait and kills an encoder that ignores quit");
            }

            Console.WriteLine($"PASS: {_checks} recorder process checks; no screen or audio captured.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable(FakeLogVariable, previousLog);
            if (createdFake) File.Delete(fake);
            if (createdFakeProbe) File.Delete(fakeProbe);
            Directory.Delete(scratch, true);
        }
    }

    internal static async Task<int> RunFakeAsync(string[] arguments)
    {
        if (arguments[0] == "progress")
        {
            File.WriteAllText(arguments[1], Environment.ProcessId.ToString());
            Console.WriteLine("frame=60\nfps=NaN\nout_time_us=1000000\nprogress=continue");
            var ready = Stopwatch.StartNew();
            while (!File.Exists(arguments[1] + ".progress-release") && ready.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
            if (!File.Exists(arguments[1] + ".progress-release")) return 9;
            await Task.Delay(650);
            Console.WriteLine("frame=90\nfps=Infinity\nout_time_us=1500000\nprogress=continue");
            Console.WriteLine("frame=2\nfps=-999\nout_time_us=-1\nprogress=end");
            return 0;
        }
        if (arguments[0] == "flood")
        {
            Console.Error.Write(new string('e', 12000) + "TAIL");
            Console.Out.Write(new string('o', 12000));
            return 7;
        }
        File.WriteAllText(arguments[1], Environment.ProcessId.ToString());
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    internal static async Task<int> RunFakeExportAsync(string[] arguments)
    {
        if (arguments.Contains("-frames:v"))
        {
            var probeLog = Environment.GetEnvironmentVariable(FakeLogVariable)!;
            var codec = arguments[Array.IndexOf(arguments, "-c:v") + 1];
            File.AppendAllText(probeLog + ".probe-order", codec + "\n");
            File.WriteAllText(probeLog, Environment.ProcessId.ToString());
            var results = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(probeLog + ".probe-results.json"))!;
            var probeMode = results.GetValueOrDefault(codec, "fail");
            if (probeMode == "wait") await Task.Delay(Timeout.InfiniteTimeSpan);
            if (probeMode != "success") Console.Error.WriteLine("Synthetic unavailable encoder: " + codec);
            return probeMode == "success" ? 0 : 9;
        }
        var source = arguments[Array.IndexOf(arguments, "-i") + 1];
        var output = arguments[^1];
        var mode = File.ReadAllText(source);
        var log = Environment.GetEnvironmentVariable(FakeLogVariable)!;
        File.WriteAllText(log + ".args.json", JsonSerializer.Serialize(arguments));
        File.WriteAllText(output, mode == "success" ? "complete fake MP4" : "partial fake MP4");
        File.WriteAllText(log, Environment.ProcessId.ToString());
        if (mode == "wait") await Task.Delay(Timeout.InfiniteTimeSpan);
        if (mode == "wait-for-release")
            while (!File.Exists(log + ".release")) await Task.Delay(20);
        if (mode == "fail") { Console.Error.Write("Synthetic export failure"); return 9; }
        return 0;
    }

    internal static async Task<int> RunFakeProbeAsync(string[] arguments)
    {
        var log = Environment.GetEnvironmentVariable(FakeLogVariable)!;
        File.WriteAllText(log + ".args.json", JsonSerializer.Serialize(arguments));
        File.WriteAllText(log, Environment.ProcessId.ToString());
        var mode = File.ReadAllText(arguments[^1]);
        if (mode == "probe-wait") await Task.Delay(Timeout.InfiniteTimeSpan);
        if (mode == "probe-flood-error")
        {
            Console.Out.Write(new string('o', 100_000));
            Console.Error.Write(new string('e', 100_000) + "TAIL");
            return 9;
        }
        if (mode == "probe-flood-success") { Console.Out.Write(new string('o', 100_000)); return 0; }
        Console.Out.Write(mode);
        return 0;
    }

    private static async Task CheckMediaToolsAsync(string scratch, string source, string log)
    {
        void ClearLog()
        {
            File.Delete(log);
            File.Delete(log + ".args.json");
            File.Delete(log + ".release");
        }
        static bool NoPartials(string folder) => !Directory.EnumerateFiles(folder, ".screendash-clip-*").Any();
        ClearLog();
        File.WriteAllText(source, """{"format":{"duration":"95.125"},"streams":[{"codec_type":"video","width":1920,"height":1080},{"codec_type":"audio"}]}""");
        var details = await MediaTools.ReadDetailsAsync(source);
        Check(details == new MediaDetails(TimeSpan.FromSeconds(95.125), 1920, 1080, true),
            "Media inspection reads duration, resolution and audio without decoding frames");
        var probeArguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(log + ".args.json"))!;
        Check(probeArguments.Contains("format=duration:stream=codec_type,width,height,duration")
            && probeArguments.Contains("json") && !probeArguments.Contains("-show_frames") && probeArguments[^1] == source,
            "Media inspection requests only compact metadata");

        File.WriteAllText(source, """{"format":{"duration":"N/A"},"streams":[{"codec_type":"video","width":1280,"height":720,"duration":"30.5"}]}""");
        details = await MediaTools.ReadDetailsAsync(source);
        Check(details == new MediaDetails(TimeSpan.FromSeconds(30.5), 1280, 720, false),
            "Media inspection falls back to video duration and detects recordings without audio");

        foreach (var invalid in new[]
        {
            """{"format":{"duration":"NaN"},"streams":[{"codec_type":"video","width":1920,"height":1080}]}""",
            """{"format":{"duration":"60"},"streams":[{"codec_type":"video","width":0,"height":1080}]}""",
            """{"format":{"duration":"60"},"streams":[{"codec_type":"audio"}]}""",
            "not valid JSON"
        })
        {
            File.WriteAllText(source, invalid);
            await ExpectIOExceptionAsync(MediaTools.ReadDetailsAsync(source));
        }
        Check(true, "Invalid duration, dimensions, missing video and malformed metadata are rejected");

        ClearLog();
        using (var cancellation = new CancellationTokenSource())
        {
            File.WriteAllText(source, "probe-wait");
            var inspection = MediaTools.ReadDetailsAsync(source, cancellation.Token);
            var pid = await WaitForPidAsync(log);
            cancellation.Cancel();
            await ExpectCancellationAsync(inspection);
            Check(!IsRunning(pid), "Cancelling media inspection terminates FFprobe");
        }
        File.WriteAllText(source, "probe-flood-error");
        try { await MediaTools.ReadDetailsAsync(source); throw new InvalidOperationException("Expected failed FFprobe."); }
        catch (IOException ex)
        {
            Check(ex.Message.EndsWith("TAIL", StringComparison.Ordinal) && ex.Message.Length < 4200,
                "Media inspection drains flooded pipes while retaining a bounded error tail");
        }
        File.WriteAllText(source, "probe-flood-success");
        await ExpectIOExceptionAsync(MediaTools.ReadDetailsAsync(source));
        Check(true, "Oversized FFprobe JSON output is rejected without unbounded memory use");

        ClearLog();
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await ExpectCancellationAsync(MediaTools.ReadDetailsAsync(source, cancellation.Token));
            await ExpectCancellationAsync(MediaTools.ExportClipAsync(source, Path.Combine(scratch, "uncalled", "clip.mp4"),
                new ClipRange(TimeSpan.Zero, TimeSpan.FromSeconds(5)), cancellation.Token));
            Check(!File.Exists(log) && !Directory.Exists(Path.Combine(scratch, "uncalled")),
                "Pre-cancelled media tools start no child and create no destination folder");
        }

        var range = new ClipRange(TimeSpan.FromSeconds(12.375), TimeSpan.FromSeconds(8.25));
        var existing = Path.Combine(scratch, "existing-clip.mp4");
        File.WriteAllText(existing, "keep this destination");
        File.WriteAllText(source, "success");
        await ExpectIOExceptionAsync(MediaTools.ExportClipAsync(source, existing, range));
        Check(File.ReadAllText(existing) == "keep this destination" && File.ReadAllText(source) == "success" && !File.Exists(log),
            "Clip export rejects existing destinations before starting FFmpeg");

        foreach (var invalid in new[]
        {
            new ClipRange(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(5)),
            new ClipRange(TimeSpan.Zero, TimeSpan.Zero),
            new ClipRange(TimeSpan.MaxValue, TimeSpan.MaxValue)
        })
        {
            try { await MediaTools.ExportClipAsync(source, Path.Combine(scratch, "invalid.mp4"), invalid); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new InvalidOperationException("Expected invalid clip range rejection.");
        }
        Check(!File.Exists(log) && !File.Exists(Path.Combine(scratch, "invalid.mp4")),
            "Negative, empty and excessive clip ranges are rejected before starting a child");

        var completed = Path.Combine(scratch, "completed-clip.mp4");
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            await MediaTools.ExportClipAsync(source, completed, range);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        var arguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(log + ".args.json"))!;
        Check(arguments[Array.IndexOf(arguments, "-ss") + 1] == "12.375" && arguments[Array.IndexOf(arguments, "-t") + 1] == "8.25"
            && Array.IndexOf(arguments, "-ss") < Array.IndexOf(arguments, "-i") && arguments[Array.IndexOf(arguments, "-c") + 1] == "copy"
            && arguments.Contains("0:a?") && arguments.Contains("-n") && !arguments.Contains("-y"),
            "Clip export uses invariant times, input seeking, optional audio and stream copy without overwrite");
        Check(File.ReadAllText(completed) == "complete fake MP4" && File.ReadAllText(source) == "success" && NoPartials(scratch),
            "Successful clip export publishes one final file and preserves its source");

        ClearLog();
        File.WriteAllText(source, "wait");
        var cancelledClip = Path.Combine(scratch, "cancelled-clip.mp4");
        using (var cancellation = new CancellationTokenSource())
        {
            var clip = MediaTools.ExportClipAsync(source, cancelledClip, range, cancellation.Token);
            var pid = await WaitForPidAsync(log);
            cancellation.Cancel();
            await ExpectCancellationAsync(clip);
            Check(!IsRunning(pid) && !File.Exists(cancelledClip) && NoPartials(scratch) && File.ReadAllText(source) == "wait",
                "Cancelled clip export removes its partial, stops FFmpeg and keeps the source");
        }

        ClearLog();
        File.WriteAllText(source, "fail");
        var failed = Path.Combine(scratch, "failed-clip.mp4");
        await ExpectIOExceptionAsync(MediaTools.ExportClipAsync(source, failed, range));
        Check(!File.Exists(failed) && NoPartials(scratch) && File.ReadAllText(source) == "fail",
            "Failed clip export removes its partial and keeps the source");

        ClearLog();
        File.WriteAllText(source, "wait-for-release");
        var competing = Path.Combine(scratch, "competing-clip.mp4");
        var competingExport = MediaTools.ExportClipAsync(source, competing, range);
        await WaitForPidAsync(log);
        File.WriteAllText(competing, "concurrent destination must survive");
        File.WriteAllText(log + ".release", "continue");
        await ExpectIOExceptionAsync(competingExport);
        Check(File.ReadAllText(competing) == "concurrent destination must survive" && NoPartials(scratch),
            "A destination created during clip export is preserved and the temporary output is removed");
        ClearLog();
    }

    private static void CheckRecentFrameRate()
    {
        var type = typeof(Recorder).Assembly.GetType("ScreenDash.RecentFrameRate")!;
        object Create(int limit = 60) => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, [limit], CultureInfo.InvariantCulture)!;
        double Update(object meter, long frames, double seconds, double fallback = 60) => (double)type.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(meter, [frames, (long)((seconds + 10) * Stopwatch.Frequency), fallback])!;
        var meter = Create();
        Check(Update(meter, 0, 0, double.NaN) == 0 && Update(meter, 0, 0, double.PositiveInfinity) == 0,
            "Recent FPS startup and repeated timestamps stay finite with invalid encoder statistics");
        meter = Create();
        Check(Update(meter, 1, 0, 500) == 60, "Startup FPS is bounded by the chosen recording target");
        Check(Math.Abs(Update(meter, 61, 1) - 60) < 0.001, "Delivered FPS uses frame increments over monotonic wall time");
        for (int second = 2; second <= 60; second++) Update(meter, 1 + second * 60, second);
        for (int second = 61; second <= 66; second++) Update(meter, 3601 + (second - 60) * 10, second);
        Check(Math.Abs(Update(meter, 3661, 66) - 10) < 0.001,
            "A recent drop to ten FPS is visible even after a minute at sixty FPS");
        for (int second = 67; second <= 72; second++) Update(meter, 3661 + (second - 66) * 60, second);
        Check(Math.Abs(Update(meter, 4021, 72) - 60) < 0.001, "Recovered frame delivery replaces the old slow window");
        Check(Update(meter, 4021, 82) == 0, "A sparse progress report with no new frames includes the stalled interval");
        Check(Update(meter, 2, 83, 3) == 3 && Math.Abs(Update(meter, 62, 84) - 60) < 0.001,
            "A reset frame counter starts a new finite measurement window");
        Check(Update(meter, 63, 0, double.NegativeInfinity) == 0,
            "A backward clock sample resets safely instead of producing a negative rate");
        meter = Create(30);
        Check(Update(meter, -1, 0, -50) == 0 && Update(meter, long.MaxValue, 1) == 30,
            "Negative counters and enormous frame jumps remain inside the configured FPS bound");
        meter = Create();
        double final = 0;
        for (int sample = 0; sample <= 10_000; sample++) final = Update(meter, sample * 6, sample / 10d);
        Check(Math.Abs(final - 60) < 0.001, "Long high-frequency progress streams retain a bounded recent measurement");
    }

    private static void CheckEncoderPreferenceKeys(string scratch, DisplaySource display)
    {
        var executable = Path.Combine(scratch, "identity.exe");
        File.WriteAllText(executable, "synthetic executable identity");
        var type = typeof(Recorder).Assembly.GetType("ScreenDash.EncoderPreferenceKey")!;
        var method = type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
        var settings = new AppSettings { Fps = 60, EncodingQuality = "Quality", AutoVideoBitrate = true };
        object? Key(string? file = null, AppSettings? options = null, DisplaySource? monitor = null) => method.Invoke(null, [file ?? executable, options ?? settings, monitor ?? display]);
        var original = Key();
        Check(original is not null && original.Equals(Key(monitor: display with { Name = "Renamed monitor" })),
            "Encoder preference identity ignores harmless display label changes");
        foreach (var changed in new[]
        {
            display with { Id = "different" }, display with { AdapterIndex = 1 }, display with { OutputIndex = 1 },
            display with { X = 100 }, display with { Y = 100 }, display with { Width = 2560 }, display with { Height = 1440 }
        })
            Check(!original!.Equals(Key(monitor: changed)), "Changed native display identity or topology invalidates the encoder preference");
        var changedSettings = settings.Clone(); changedSettings.Fps = 30;
        Check(!original!.Equals(Key(options: changedSettings)), "Changed recording cadence requires a fresh normal encoder choice");
        changedSettings = settings.Clone(); changedSettings.EncodingQuality = "Efficiency";
        Check(!original.Equals(Key(options: changedSettings)), "Changed encoder quality invalidates the preferred configuration");
        changedSettings = settings.Clone(); changedSettings.AutoVideoBitrate = false; changedSettings.VideoBitrateKbps = 5000;
        Check(!original.Equals(Key(options: changedSettings)), "Changed resolved bitrate invalidates the preferred configuration");
        var otherPath = Path.Combine(scratch, "another-identity.exe"); File.Copy(executable, otherPath);
        Check(!original.Equals(Key(otherPath)), "A different FFmpeg path cannot reuse another executable's preference");
        File.SetLastWriteTimeUtc(executable, File.GetLastWriteTimeUtc(executable).AddSeconds(10));
        Check(!original.Equals(Key()), "Replacing the FFmpeg timestamp invalidates the in-memory preference");
        var beforeLength = Key(); File.AppendAllText(executable, " changed");
        Check(!beforeLength!.Equals(Key()), "Changing the FFmpeg size invalidates its preference");
        Check(Key(Path.Combine(scratch, "missing-identity.exe")) is null, "Missing executable metadata disables the optional preference safely");
    }

    private static async Task CheckEncoderPreferenceAsync(string executable, DisplaySource display, string log)
    {
        var method = typeof(Recorder).GetMethod("SelectEncoderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var settings = new AppSettings { EncoderMode = "Auto", AllowSoftwareEncoding = false, AudioEnabled = false };
        void Configure(params (string Codec, string Result)[] results)
        {
            File.Delete(log); File.Delete(log + ".probe-order");
            File.WriteAllText(log + ".probe-results.json", JsonSerializer.Serialize(results.ToDictionary(item => item.Codec, item => item.Result)));
        }
        string[] Order() => File.Exists(log + ".probe-order") ? File.ReadAllLines(log + ".probe-order") : [];
        async Task<string> Select(Recorder recorder, AppSettings? options = null, CancellationToken token = default)
        {
            var task = (Task)method.Invoke(recorder, [executable, options ?? settings, display, token])!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            return (string)result.GetType().GetProperty("Codec")!.GetValue(result)!;
        }
        using var recorder = new Recorder();
        Configure(("h264_amf", "success"));
        Check(await Select(recorder) == "h264_amf" && Order().SequenceEqual(["h264_nvenc", "h264_amf"]),
            "A cold automatic start probes normal hardware candidates until one succeeds");
        Configure(("h264_amf", "success"));
        Check(await Select(recorder) == "h264_amf" && Order().SequenceEqual(["h264_amf"]),
            "A subsequent start freshly probes the proven hardware first without repeating unavailable GPUs");
        Configure(("h264_nvenc", "success"));
        Check(await Select(recorder) == "h264_nvenc" && Order().SequenceEqual(["h264_amf", "h264_nvenc"]),
            "A failed preferred probe is invalidated before the normal remaining candidates are tried once");
        Configure(("h264_qsv", "success"));
        Check(await Select(recorder) == "h264_qsv" && Order().SequenceEqual(["h264_nvenc", "h264_amf", "h264_qsv"]),
            "Fallback probing never repeats a failed preferred candidate");
        var explicitNvidia = settings.Clone(); explicitNvidia.EncoderMode = "NVIDIA";
        Configure(("h264_qsv", "success"));
        try { await Select(recorder, explicitNvidia); throw new Exception("Explicit encoder unexpectedly fell back."); }
        catch (InvalidOperationException)
        { Check(Order().SequenceEqual(["h264_nvenc"]), "An explicit encoder ignores cached alternatives and never silently changes vendor"); }
        Configure(("libx264", "success"));
        try { await Select(recorder); throw new Exception("Automatic software unexpectedly allowed."); }
        catch (InvalidOperationException)
        { Check(!Order().Contains("libx264"), "Hardware failure never enters software encoding without explicit permission"); }
        var software = settings.Clone(); software.EncoderMode = "Software";
        Configure(("libx264", "success"));
        Check(await Select(recorder, software) == "libx264" && Order().SequenceEqual(["libx264"]),
            "Explicit software selection is a deliberate CPU choice and ignores hardware preferences");
        var allowed = settings.Clone(); allowed.AllowSoftwareEncoding = true;
        Configure(("libx264", "success"));
        Check(await Select(recorder, allowed) == "libx264" && Order().SequenceEqual(["h264_nvenc", "h264_amf", "h264_qsv", "libx264"]),
            "Optional CPU fallback remains last after all fresh hardware probes fail");
        Configure(("h264_amf", "success"), ("libx264", "success"));
        Check(await Select(recorder) == "h264_amf" && Order().SequenceEqual(["h264_nvenc", "h264_amf"]),
            "A software fallback is never retained as the preferred automatic encoder");
        var lowerFps = settings.Clone(); lowerFps.Fps = 30;
        Configure(("h264_amf", "success"));
        Check(await Select(recorder, lowerFps) == "h264_amf" && Order().SequenceEqual(["h264_nvenc", "h264_amf"]),
            "Changed capture settings begin a normal fresh hardware selection");
        Configure(("h264_amf", "wait"));
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = Select(recorder, lowerFps, cancellation.Token);
            var pid = await WaitForPidAsync(log); cancellation.Cancel(); await ExpectCancellationAsync(pending);
            Check(Order().SequenceEqual(["h264_amf"]) && !IsRunning(pid), "Cancelling a preferred hardware probe terminates it without trying other candidates");
        }
        Configure(("h264_amf", "success"));
        Check(await Select(recorder, lowerFps) == "h264_amf" && Order().SequenceEqual(["h264_amf"]),
            "Cancellation does not discard a previously proven hardware preference");
        File.Delete(log); File.Delete(log + ".probe-order"); File.Delete(log + ".probe-results.json");
    }

    private static async Task CheckBeforeCaptureAsync(string scratch, DisplaySource display, string log)
    {
        IReadOnlyList<DisplaySource> currentDisplays = [display];
        Recorder Create() => (Recorder)Activator.CreateInstance(typeof(Recorder), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [new Func<IReadOnlyList<DisplaySource>>(() => currentDisplays)], CultureInfo.InvariantCulture)!;
        var directory = Path.Combine(scratch, "capture-gate");
        var settings = new AppSettings { RecordingDirectory = directory, AudioEnabled = false, MicrophoneEnabled = false, EncoderMode = "Auto" };
        File.WriteAllText(log + ".probe-results.json", "{\"h264_nvenc\":\"success\"}");
        bool BeforeResources(Recorder recorder) => recorder.SessionPrefix is null && recorder.ProcessId is null
            && new[] { "_audio", "_microphone", "_process" }.All(name => typeof(Recorder).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(recorder) is null)
            && !Directory.EnumerateFiles(directory).Any();
        using (var recorder = Create())
        {
            int calls = 0; bool cleanBoundary = false;
            try
            {
                await recorder.StartAsync(settings, display, "rejected", beforeCapture: token =>
                { calls++; cleanBoundary = BeforeResources(recorder) && token.CanBeCanceled; throw new InvalidOperationException("Synthetic capture permission rejected"); });
                throw new Exception("Rejected capture callback unexpectedly started recording.");
            }
            catch (InvalidOperationException ex) when (ex.Message == "Synthetic capture permission rejected") { }
            Check(calls == 1 && cleanBoundary && BeforeResources(recorder) && !recorder.Status.Recording,
                "The real StartAsync reaches a rejecting gate after a successful synthetic probe and before session, audio, capture process or output");
        }
        using (var recorder = Create())
        using (var cancellation = new CancellationTokenSource())
        {
            bool called = false;
            await ExpectCancellationAsync(recorder.StartAsync(settings, display, "cancelled_after_gate", cancellation.Token, token =>
            { called = true; cancellation.Cancel(); return Task.CompletedTask; }));
            Check(called && BeforeResources(recorder), "Cancellation is rechecked after a successful capture gate before resources are created");
        }
        using (var recorder = Create())
        {
            bool called = false;
            await ExpectIOExceptionAsync(recorder.StartAsync(settings, display, "changed_display_at_gate", beforeCapture: token =>
            { called = true; currentDisplays = [display with { Width = 2560 }]; return Task.CompletedTask; }));
            Check(called && BeforeResources(recorder), "A display changed during the final asynchronous gate is rejected before recording resources or output");
            currentDisplays = [display];
        }
        using (var recorder = Create())
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var starting = recorder.StartAsync(settings, display, "stopped_at_gate", beforeCapture: async token =>
            { entered.TrySetResult(true); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = recorder.StopAsync();
            await ExpectCancellationAsync(starting); await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Check(BeforeResources(recorder), "Stop cancels a pending final capture gate without creating any recording resources");
        }
        File.Delete(log); File.Delete(log + ".probe-order"); File.Delete(log + ".probe-results.json");
    }

    private static async Task CheckProgressAsync(string apphost, string log)
    {
        using var recorder = new Recorder();
        using var process = StartFake(apphost, "progress", log, redirectOutput: true);
        typeof(Recorder).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(recorder, process);
        var updates = new List<RecorderStatus>();
        recorder.Changed += status => { updates.Add(status); if (updates.Count == 1) File.WriteAllText(log + ".progress-release", "ready"); };
        var firstFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = (Task)typeof(Recorder).GetMethod("ReadProgressAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(recorder, [process, "Synthetic hardware", DateTime.UtcNow, firstFrame, 60])!;
        await read; await process.WaitForExitAsync();
        Check(firstFrame.Task.IsCompletedSuccessfully && updates.Count == 3 && updates.All(status => double.IsFinite(status.Fps) && status.Fps is >= 0 and <= 60),
            "Fake FFmpeg progress keeps startup, invalid averages and counter resets finite without capture devices");
        Check(updates[1].Fps > 0 && updates[1].VideoElapsed == TimeSpan.FromSeconds(1.5)
            && updates[^1].Frames == 2 && updates[^1].Fps == 0 && updates[^1].VideoElapsed == TimeSpan.Zero,
            "Progress parsing reports recent delivery while preserving bounded media time and reset frames");
        File.Delete(log);
        File.Delete(log + ".progress-release");
    }

    private static Process StartFake(string apphost, string mode, string log, bool redirectOutput = false)
    {
        var start = new ProcessStartInfo(apphost) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = redirectOutput };
        start.ArgumentList.Add("--fake-ffmpeg");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(log);
        return Process.Start(start) ?? throw new IOException("Cannot start fake encoder.");
    }

    private static Task<(bool Success, string Error)> ProbeAsync(string executable, List<string> arguments, CancellationToken ct) =>
        (Task<(bool Success, string Error)>)typeof(Recorder).GetMethod("ProbeAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [executable, arguments, ct])!;

    private static void ValidateSnapshot(DisplaySource selected, IReadOnlyList<DisplaySource> current)
    {
        try { typeof(DisplayCatalog).GetMethod("ValidateSelection", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [selected, current]); }
        catch (TargetInvocationException ex) when (ex.InnerException is IOException) { throw ex.InnerException; }
    }

    private static void ExpectDisplayFailure(DisplaySource selected, IReadOnlyList<DisplaySource> current)
    {
        try { ValidateSnapshot(selected, current); }
        catch (IOException) { Check(true, "Changed or missing display cannot silently substitute another screen"); return; }
        throw new InvalidOperationException("A changed display selection was accepted.");
    }

    private static async Task<int> WaitForPidAsync(string path)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(5))
        {
            try { if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out var pid)) return pid; }
            catch (IOException) { }
            await Task.Delay(20);
        }
        throw new TimeoutException("The fake FFmpeg did not report its PID.");
    }

    private static bool IsRunning(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task ExpectCancellationAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected cancellation.");
    }

    private static async Task ExpectIOExceptionAsync(Task task)
    {
        try { await task; }
        catch (IOException) { return; }
        throw new InvalidOperationException("Expected an I/O failure.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine("PASS: " + description);
    }
}
