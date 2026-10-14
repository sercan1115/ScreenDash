using System.Diagnostics;
using System.Reflection;
using ScreenDash;

/// <summary>Synthetic PCM and FFmpeg checks. Never enumerates or opens a microphone or desktop.</summary>
internal static class AudioChecks
{
    private static string Graph(bool system, bool mic, int systemGain = 100, int micGain = 100)
        => (string)typeof(Recorder).GetMethod("BuildAudioFilterGraph", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [system, mic, systemGain, micGain])!;

    public static async Task<int> RunAsync()
    {
        var passed = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed++;
            Console.WriteLine("PASS " + description);
        }
        Check(!new AppSettings().MicrophoneEnabled, "Microphone is disabled by default");
        Check(Graph(false, false).Length == 0, "Disabled audio creates no audio graph");
        Check(Graph(true, false).Contains("[0:a:0]") && !Graph(true, false).Contains("amix="), "System-only capture needs one input");
        Check(Graph(false, true, 100, 125).Contains("[0:a:0]") && Graph(false, true, 100, 125).Contains("volume=1.25"), "Microphone-only gain uses input zero");
        var combined = Graph(true, true, 50, 150);
        Check(combined.Contains("[1:a:0]") && combined.Contains("normalize=0") && combined.Contains("level=false"), "Mixed audio preserves source volume and limits without amplification");
        Check(combined.Contains("aresample=48000") && combined.Contains("channel_layouts=stereo"), "Every mixed source normalizes its audio format before mixing");
        try { Graph(true, true, -1); throw new InvalidOperationException("Invalid gain accepted"); }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException)
        { passed++; Console.WriteLine("PASS Invalid gain rejected"); }

        var bufferType = typeof(Recorder).Assembly.GetType("ScreenDash.ClockedAudioBuffer")!;
        var ring = Activator.CreateInstance(bufferType, [4, 2])!;
        var write = bufferType.GetMethod("Write")!;
        var read = bufferType.GetMethod("Read")!;
        var output = new byte[8];
        void Read(long start) => read.Invoke(ring, [start, output, 4]);
        Read(0);
        Check(output.All(b => b == 0), "Empty timeline emits silence");
        write.Invoke(ring, [0L, new byte[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 }, 6]);
        Read(0);
        Check(output.SequenceEqual(new byte[] { 0, 0, 0, 0, 3, 3, 4, 4 }), "Oversized callbacks retain newest frames and do not repeat stale audio");
        Read(2);
        Check(output.SequenceEqual(new byte[] { 3, 3, 4, 4, 5, 5, 6, 6 }), "Wrapped newest frames preserve their order");
        write.Invoke(ring, [8L, new byte[] { 9, 9, 10, 10 }, 2]);
        Read(6);
        Check(output.SequenceEqual(new byte[] { 0, 0, 0, 0, 9, 9, 10, 10 }), "Real callback gaps remain silent without shrinking the timeline");
        Read(4);
        Check(output.All(b => b == 0), "Overwritten timeline positions never replay future samples");
        Check((int)bufferType.GetProperty("CapacityFrames")!.GetValue(ring)! == 4, "Large callbacks keep buffer capacity fixed");

        using (var recorder = new Recorder())
        {
            var fields = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(Recorder).GetField("_audioGeneration", fields)!.SetValue(recorder, 3L);
            typeof(Recorder).GetField("_status", fields)!.SetValue(recorder, new RecorderStatus(true, "Test", "Aufnahme läuft", 15, 10, DateTime.UtcNow));
            var frame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            typeof(Recorder).GetField("_firstFrame", fields)!.SetValue(recorder, frame);
            var fault = typeof(Recorder).GetMethod("AudioSourceFailed", fields)!;
            fault.Invoke(recorder, [2L, "Mikrofon", new IOException("old device")]);
            Check(recorder.Status.Recording && !frame.Task.IsCompleted, "Old microphone callbacks cannot fault a new capture");
            var lateStop = (Task)typeof(Recorder).GetMethod("StopAfterFailureAsync", fields)!.Invoke(recorder, [2L])!;
            await lateStop;
            Check(recorder.Status.Recording, "Delayed audio shutdown cannot stop a new recording generation");
            fault.Invoke(recorder, [3L, "Mikrofon", new IOException("device removed")]);
            Check(!recorder.Status.Recording && recorder.Status.AudioWarning.Contains("device removed") && frame.Task.IsFaulted,
                "Current endpoint failures stop safely and retain a visible warning");
            _ = frame.Task.Exception;
        }
        Console.WriteLine($"{passed} audio checks passed; FFmpeg and capture devices were not used.");
        return 0;
    }

    public static async Task<int> RunIntegrationAsync()
    {
        try { _ = Recorder.FindFfmpeg(); }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine("--audio-integration requires FFmpeg in PATH or beside the app. Use --audio-checks for the 16 device-free checks that run without FFmpeg.");
            return 1;
        }
        var passed = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed++;
            Console.WriteLine("PASS " + description);
        }

        // FFmpeg uses generated sine waves, not user audio or capture devices.
        var micOnly = await RenderAsync(Graph(false, true, 100, 150), ["sine=frequency=440:sample_rate=44100:duration=0.25"]);
        Check(micOnly.Length is >= 11800 * 2 and <= 12200 * 2, "Mono microphone is converted to 48 kHz stereo");
        Check(micOnly.Where((_, i) => i % 2 == 0).Zip(micOnly.Where((_, i) => i % 2 != 0)).All(p => Math.Abs(p.First - p.Second) < 0.00001), "Mono microphone appears in both output channels");
        var baseline = await RenderAsync(Graph(true, false), ["sine=frequency=440:sample_rate=48000:duration=0.25"]);
        var silentMic = await RenderAsync(Graph(true, true), ["sine=frequency=440:sample_rate=48000:duration=0.25", "anullsrc=r=44100:cl=mono:d=0.25"]);
        Check(Math.Abs(Rms(baseline) - Rms(silentMic)) < 0.005, "A silent microphone does not halve game audio");
        var muted = await RenderAsync(Graph(true, false, 0), ["sine=frequency=440:sample_rate=48000:duration=0.25"]);
        Check(muted.All(value => Math.Abs(value) < 0.000001), "Zero gain produces silence");
        var loud = await RenderAsync(Graph(true, true, 200, 200), ["sine=frequency=440:sample_rate=48000:duration=0.25,volume=8", "sine=frequency=440:sample_rate=44100:duration=0.25,volume=8"]);
        Check(loud.All(value => float.IsFinite(value) && Math.Abs(value) <= 0.951), "Loud mixed sources stay finite and below the clipping limit");
        Check(Rms(loud) > 0.1, "Limiter retains audible audio");
        Console.WriteLine($"{passed} synthetic FFmpeg audio integration checks passed; no capture devices were opened.");
        return 0;
    }

    private static double Rms(float[] values) => Math.Sqrt(values.Sum(value => (double)value * value) / values.Length);

    private static async Task<float[]> RenderAsync(string graph, string[] sources)
    {
        var start = new ProcessStartInfo(Recorder.FindFfmpeg())
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin" }) start.ArgumentList.Add(arg);
        foreach (var source in sources)
            foreach (var arg in new[] { "-f", "lavfi", "-i", source }) start.ArgumentList.Add(arg);
        foreach (var arg in new[] { "-filter_complex", graph, "-map", "[sound]", "-t", "0.25", "-c:a", "pcm_f32le", "-f", "f32le", "pipe:1" }) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (!process.Start()) throw new IOException("Synthetic FFmpeg check could not start.");
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        using var bytes = new MemoryStream();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(bytes, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var error = await errors;
            if (process.ExitCode != 0) throw new IOException(error);
            if (bytes.Length == 0 || bytes.Length > 1024 * 1024 || bytes.Length % sizeof(float) != 0)
                throw new IOException("Unexpected synthetic audio size.");
            var samples = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes.ToArray(), 0, samples, 0, (int)bytes.Length);
            return samples;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
}
