using System.Diagnostics;
using System.Reflection;
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
        var previousLog = Environment.GetEnvironmentVariable(FakeLogVariable);
        var createdFake = false;
        Directory.CreateDirectory(scratch);
        try
        {
            Check(File.Exists(apphost), "Test apphost is available");
            if (File.Exists(fake)) throw new IOException("Process checks require an isolated test output directory without ffmpeg.exe.");
            File.Copy(apphost, fake, false);
            createdFake = true;
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
        File.WriteAllText(output, mode == "success" ? "complete fake MP4" : "partial fake MP4");
        File.WriteAllText(Environment.GetEnvironmentVariable(FakeLogVariable)!, Environment.ProcessId.ToString());
        if (mode == "wait") await Task.Delay(Timeout.InfiniteTimeSpan);
        if (mode == "fail") { Console.Error.Write("Synthetic export failure"); return 9; }
        return 0;
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
