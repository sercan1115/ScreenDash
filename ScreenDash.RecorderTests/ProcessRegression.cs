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

    private static Process StartFake(string apphost, string mode, string log)
    {
        var start = new ProcessStartInfo(apphost) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
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
