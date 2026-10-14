using System.Text.Json;
using ScreenDash;

if (args.Length > 0 && args[0] == "--scheduling-checks") return await SchedulingChecks.RunAsync();
if (args.Length > 0 && args[0] == "--feature-checks") return await ControllerFeatureChecks.RunAsync();

string directory = Path.GetFullPath(args.Length > 0 ? args[0] : "work/controller-integration");
Directory.CreateDirectory(directory);
bool timerTest = args.Length > 1 && args[1] == "timer";
var settings = new AppSettings { RecordingDirectory = directory, ShowNotifications = false, AutoStopMinutes = timerTest ? 1 : 0 };
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
    await controller.AddBookmarkAsync("Testmoment");
    var liveFile = controller.GetRecordings().Single();
    var bookmark = (await controller.GetBookmarksAsync(liveFile.Path)).Single();
    Check(bookmark.Label == "Testmoment" && bookmark.Offset > TimeSpan.Zero && liveFile.Protected, "Live bookmark has a video offset and protects the entire recording");
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
    var details = await controller.GetMediaDetailsAsync(first.Path);
    Check(details.Duration > TimeSpan.Zero && details.HasAudio && details.Width > 0, "Completed recording exposes duration, dimensions and audio");
    Check(bookmark.Offset < details.Duration, "Live bookmark remains inside the finalized video");
    var timeMatch = await controller.FindByTimeAsync(first.StartedUtc.AddMilliseconds(500).ToLocalTime());
    Check(timeMatch?.Recording.Path == first.Path && Math.Abs(timeMatch.Offset.TotalMilliseconds - 500) < 1, "Date and time search locates the finalized recording and exact relative offset");
    await controller.RenameBookmarkAsync(first.Path, bookmark.Id, "Umbenannt");
    Check((await controller.GetBookmarksAsync(first.Path)).Single().Label == "Umbenannt", "Bookmark name persists through controller integration");
    string clip = Path.Combine(directory, "short-clip.mp4");
    await controller.ExportClipAsync(first.Path, clip, new ClipRange(TimeSpan.Zero, TimeSpan.FromSeconds(Math.Min(1, details.Duration.TotalSeconds))));
    Check(new FileInfo(clip).Length > 0 && File.Exists(first.Path), "Clip export keeps the original recording");
    try { await controller.ExportClipAsync(first.Path, Path.Combine(directory, "invalid.mp4"), new ClipRange(details.Duration, TimeSpan.FromSeconds(1))); throw new Exception("Invalid clip must be rejected."); }
    catch (ArgumentOutOfRangeException) { Check(!File.Exists(Path.Combine(directory, "invalid.mp4")), "Out-of-range clips create no output"); }
    await controller.ResumeAsync();
    Check(controller.Status.Recording && !controller.Status.Paused, "Resume starts a fresh recording session");
    Check(controller.Status.Elapsed >= pausedElapsed, "Resume continues the session timer");
    await Task.Delay(2500);
    if (timerTest)
    {
        var deadline = DateTime.UtcNow.AddSeconds(75);
        while ((controller.Status.Armed || controller.Status.Recording || controller.Status.Busy) && DateTime.UtcNow < deadline) await Task.Delay(250);
        Console.WriteLine($"Timer complete: armed={controller.Status.Armed}, recording={controller.Status.Recording}, busy={controller.Status.Busy}, message={controller.Status.Message}");
        Check(!controller.Status.Armed && !controller.Status.Recording && controller.Status.Message.Contains("Zeitlimit"), "One-minute active recording timer stops the real encoder automatically");
    }
    await controller.StopAsync();
    Check(!controller.Status.Recording && !controller.Status.Armed && !controller.Status.Paused, "Stop disarms the session");
    var clips = controller.GetRecordings();
    Check(clips.Count == 2 && clips.All(f => !f.Active), "Pause and resume create two independent segments");
    await controller.SetProtectedManyAsync(clips.Select(f => f.Path).ToArray(), false);
    Check(controller.GetRecordings().All(f => !f.Protected), "Batch protection update persists");
    int deleted = await controller.DeleteRecordingsAsync([first.Path]);
    Check(deleted == 1 && !File.Exists(first.Path) && !File.Exists(first.Path + ".note.json") && !File.Exists(first.Path + ".bookmarks.json"), "Manual deletion removes video and both owned metadata sidecars");
    Check(File.Exists(export), "Export survives original video deletion");
    File.WriteAllText(Path.Combine(directory, "controller-result.json"), JsonSerializer.Serialize(new { success = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { await controller.StopAsync(); }
return 0;
