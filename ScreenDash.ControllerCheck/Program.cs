using System.Text.Json;
using ScreenDash;

string directory = Path.GetFullPath(args.Length > 0 ? args[0] : "work/controller-integration");
Directory.CreateDirectory(directory);
var settings = new AppSettings { RecordingDirectory = directory, ShowNotifications = false };
using var controller = new AppController(settings);
var checks = new List<string>();
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks.Add(description); Console.WriteLine("PASS " + description);
}
try
{
    Check(!controller.Status.Recording && !controller.Status.Armed, "Initialization doesn't arm recording");
    Check(controller.Displays.Count > 0, "Real display detected");
    await controller.ToggleRecordingAsync();
    Check(controller.Status.Recording && controller.Status.Armed, "Start records video and audio");
    var started = controller.Status.Elapsed;
    await controller.ResumeAsync();
    Check(controller.Status.Recording && controller.Status.Elapsed >= started, "Resume on active recording is harmless");
    await Task.Delay(2500);
    await controller.ProtectRecentAsync();
    Check(controller.GetRecordings().Any(f => f.Protected && f.Active), "Recent protection includes current segment");
    await controller.PauseAsync();
    Check(!controller.Status.Recording && controller.Status.Armed && controller.Status.Paused, "Manual pause stops encoder while preserving intent");
    var pausedElapsed = controller.Status.Elapsed;
    var first = controller.GetRecordings().Single();
    Check(!first.Active && first.Bytes > 0, "Pause finalizes the previous segment");
    await controller.SetLabelAsync(first.Path, "Vor der Pause");
    Check(controller.GetRecordings().Single().Label == "Vor der Pause", "Label persists in archive");
    await Task.Delay(1500);
    Check(controller.GetRecordings().Count == 1 && controller.Status.Paused, "Paused interval produces no new video");
    Check(pausedElapsed > TimeSpan.Zero && controller.Status.Elapsed == pausedElapsed, "Session timer stays frozen during pause");
    string export = Path.Combine(directory, "pause-export.mp4");
    await controller.ExportAsync(first.Path, export);
    Check(new FileInfo(export).Length > 0, "Closed segment exports as MP4 while paused");
    await controller.ResumeAsync();
    Check(controller.Status.Recording && !controller.Status.Paused, "Resume starts a fresh recording session");
    Check(controller.Status.Elapsed >= pausedElapsed, "Resume continues the session timer");
    await Task.Delay(2500);
    await controller.StopAsync();
    Check(!controller.Status.Recording && !controller.Status.Armed && !controller.Status.Paused, "Stop disarms the session");
    var clips = controller.GetRecordings();
    Check(clips.Count == 2 && clips.All(f => !f.Active), "Pause and resume create two independent segments");
    await controller.SetProtectedManyAsync(clips.Select(f => f.Path).ToArray(), false);
    Check(controller.GetRecordings().All(f => !f.Protected), "Batch protection update persists");
    int deleted = await controller.DeleteRecordingsAsync([first.Path]);
    Check(deleted == 1 && !File.Exists(first.Path) && !File.Exists(first.Path + ".note.json"), "Manual deletion removes video and own label sidecar");
    Check(File.Exists(export), "Export survives original video deletion");
    File.WriteAllText(Path.Combine(directory, "controller-result.json"), JsonSerializer.Serialize(new { success = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { await controller.StopAsync(); }
