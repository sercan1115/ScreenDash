using System.Diagnostics;
using System.Text.Json;

namespace ScreenDash;

internal static class SmokeTest
{
    public static async Task<int> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var result = new Dictionary<string, object?>();
        using var recorder = new Recorder();
        try
        {
            var displays = DisplayCatalog.GetDisplays();
            var audio = AudioLoopback.GetDevices();
            result["displays"] = displays;
            result["audioDevices"] = audio;
            if (displays.Count == 0) throw new InvalidOperationException("Kein Bildschirm verfügbar.");
            var settings = new AppSettings { RecordingDirectory = directory, AudioEnabled = true, Fps = 15, VideoBitrateKbps = 3000 };
            string prefix = "screendash_" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'") + "_" + Guid.NewGuid().ToString("N")[..8];
            var timer = Stopwatch.StartNew();
            await recorder.StartAsync(settings, displays[0], prefix);
            result["startupSeconds"] = timer.Elapsed.TotalSeconds;
            result["encoder"] = recorder.Status.Encoder;
            using var self = Process.GetCurrentProcess();
            Process? child = recorder.ProcessId is int pid ? Process.GetProcessById(pid) : null;
            using (child)
            {
                var cpuBefore = self.TotalProcessorTime + (child?.TotalProcessorTime ?? TimeSpan.Zero);
                timer.Restart();
                await Task.Delay(12_000);
                self.Refresh(); child?.Refresh();
                result["sampleSeconds"] = timer.Elapsed.TotalSeconds;
                result["cpuPercentOfMachine"] = ((self.TotalProcessorTime + (child?.TotalProcessorTime ?? TimeSpan.Zero)) - cpuBefore).TotalSeconds / timer.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
                result["workingSetMiB"] = (self.WorkingSet64 + (child?.WorkingSet64 ?? 0)) / 1048576.0;
                result["statusBeforeStop"] = recorder.Status;
                if (!recorder.Status.Recording) throw new InvalidOperationException("Aufnahme während des Tests abgebrochen: " + recorder.Status.Message);
            }
            await recorder.StopAsync();
            var files = Directory.GetFiles(directory, prefix + "_*.mkv");
            if (files.Length == 0 || files.All(f => new FileInfo(f).Length == 0)) throw new InvalidOperationException("Keine Videodaten geschrieben.");
            result["files"] = files;
            await Recorder.ExportAsync(files[0], Path.Combine(directory, "export-test.mp4"));
            result["exportBytes"] = new FileInfo(Path.Combine(directory, "export-test.mp4")).Length;
            result["success"] = true;
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            result["success"] = false; result["error"] = ex.ToString();
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally { await recorder.StopAsync(); }
    }
}
