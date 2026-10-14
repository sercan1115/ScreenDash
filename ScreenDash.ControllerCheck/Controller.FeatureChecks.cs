using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ScreenDash;

/// <summary>Controller policy checks with fake game snapshots and HTTP; never starts capture or an update.</summary>
internal static class ControllerFeatureChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task<int> RunAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ScreenDash-controller-features-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var passed = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed++; Console.WriteLine("PASS " + description);
        }
        IReadOnlyList<GameProcess> running = [new("arena.exe", 123)];
        bool blockSnapshot = false;
        using var snapshotEntered = new ManualResetEventSlim();
        using var snapshotRelease = new ManualResetEventSlim();
        var games = new GameDetection(() =>
        {
            if (blockSnapshot)
            {
                snapshotEntered.Set();
                if (!snapshotRelease.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fake game snapshot did not release.");
            }
            return running;
        }, 123);
        using var handler = new FakeUpdateHandler();
        using var client = new HttpClient(handler);
        try
        {
            using var controller = new AppController(new AppSettings
            {
                RecordingDirectory = folder, ShowNotifications = false,
                GameDetectionEnabled = false, GameExecutables = ["arena.exe"], SilenceWarningSeconds = 10
            }, new ProfileLibrary(Path.Combine(folder, "profiles.json")), games, new UpdateService(client));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (controller.Status.Busy && DateTime.UtcNow < deadline) await Task.Delay(10);
            if (controller.Status.Busy) throw new TimeoutException("Controller storage initialization did not finish.");

            var session = (RecordingSessionState)Field("_session").GetValue(controller)!;
            var recorder = (Recorder)Field("_recorder").GetValue(controller)!;
            async Task CheckGamesAsync() => await (Task)Method("CheckGamesAsync").Invoke(controller, null)!;
            void Silence() => Method("CheckSilenceWarning").Invoke(controller, null);
            void Settings(Action<AppSettings> edit)
            {
                var settings = controller.Settings;
                edit(settings); Field("_settings").SetValue(controller, settings);
            }
            void Recording(double peak = 0) => Field("_recordStatus").SetValue(controller,
                new RecorderStatus(true, "Synthetic", "Synthetic policy status", 15, 150, DateTime.UtcNow, AudioPeak: peak));
            long OldCounter() => Stopwatch.GetTimestamp() - Stopwatch.Frequency * 20L;
            void SeedSilence()
            {
                Field("_silentSinceCounter").SetValue(controller, OldCounter());
                Field("_silenceWarning").SetValue(controller, "old warning");
            }

            await controller.StopAsync();
            Check(!games.Evaluate(["arena.exe"]).ShouldStart, "Explicit Stop suppresses automatic restart while the selected game stays open");
            running = []; _ = games.Evaluate(["arena.exe"]);
            running = [new("arena.exe", 123)];
            Check(games.Evaluate(["arena.exe"]).ShouldStart, "Automatic start is available again after all selected games exit");

            running = []; games.Reset();
            Settings(settings => settings.GameDetectionEnabled = true);
            session.Start();
            await CheckGamesAsync();
            Check(session.Armed && !Field("_gameRecordingOwned").GetValue(controller)!.Equals(true) && recorder.ProcessId is null,
                "A manually armed session is not stopped by game detection when no game runs");
            session.Stop();

            Field("_locked").SetValue(controller, true);
            session.SetPrivacy(true, false, true);
            running = [new("arena.exe", 123)]; games.Reset();
            await CheckGamesAsync();
            Check(!session.Armed && recorder.ProcessId is null && Field("_pendingStart").GetValue(controller) is null,
                "A detected game cannot start recording on a locked PC");

            // Keep privacy locked even if stale-result validation regresses. A changed exit counter
            // reveals that a stale snapshot was applied without ever opening capture devices.
            var marker = OldCounter();
            Field("_gameExitCounter").SetValue(controller, marker);
            blockSnapshot = true; snapshotEntered.Reset(); snapshotRelease.Reset();
            var staleSettings = CheckGamesAsync();
            if (!snapshotEntered.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Fake snapshot did not start.");
            Settings(settings => settings.GameExecutables = ["other.exe"]);
            snapshotRelease.Set(); await staleSettings.WaitAsync(TimeSpan.FromSeconds(2)); blockSnapshot = false;
            Check((long?)Field("_gameExitCounter").GetValue(controller) == marker && !session.Armed,
                "A game snapshot with superseded settings cannot change controller state");

            Settings(settings => settings.GameExecutables = ["arena.exe"]);
            marker = OldCounter(); Field("_gameExitCounter").SetValue(controller, marker);
            blockSnapshot = true; snapshotEntered.Reset(); snapshotRelease.Reset();
            var staleStop = CheckGamesAsync();
            if (!snapshotEntered.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Fake snapshot did not start.");
            await controller.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
            Check(!staleStop.IsCompleted && !session.Armed, "Stop completes while a game process snapshot is blocked");
            snapshotRelease.Set(); await staleStop.WaitAsync(TimeSpan.FromSeconds(2)); blockSnapshot = false;
            Check(Field("_gameExitCounter").GetValue(controller) is null && recorder.ProcessId is null && !games.Evaluate(["arena.exe"]).ShouldStart,
                "A snapshot from before Stop cannot change controller state afterward");

            Field("_locked").SetValue(controller, false); session.SetPrivacy(false, false, true);
            session.Start(); session.Pause(); games.MarkRecordingStarted(); Field("_gameRecordingOwned").SetValue(controller, true);
            await CheckGamesAsync();
            Check(session.Armed && session.ManualPaused && recorder.ProcessId is null, "A running game cannot undo an intentional pause of its automatic recording");
            running = []; Field("_gameExitCounter").SetValue(controller, OldCounter());
            await CheckGamesAsync();
            Check(!session.Armed && !controller.Status.Recording && controller.Status.Message.Contains("Spiel beendet"), "Game exit stops its owned paused session after the grace period");

            Settings(settings => { settings.GameDetectionEnabled = false; settings.AudioEnabled = true; settings.WarnOnSilence = true; settings.SystemGainPercent = 100; });
            Recording(); Silence();
            Check(Field("_silentSinceCounter").GetValue(controller) is long && controller.Status.AudioWarning.Length == 0,
                "Silent game audio starts a grace period instead of warning immediately");
            Field("_silentSinceCounter").SetValue(controller, OldCounter()); Silence();
            Check(controller.Status.AudioWarning.Contains("Kein Spielton"), "Game audio warns after the configured silence threshold");
            Recording(.2); Silence();
            Check(controller.Status.AudioWarning.Length == 0 && Field("_silentSinceCounter").GetValue(controller) is null,
                "Audible game audio clears the warning and silence timer");
            Recording(); SeedSilence(); Settings(settings => settings.WarnOnSilence = false); Silence();
            Check(controller.Status.AudioWarning.Length == 0 && Field("_silentSinceCounter").GetValue(controller) is null,
                "Disabling audio warnings clears an existing silence warning");
            SeedSilence(); Settings(settings => { settings.WarnOnSilence = true; settings.AudioEnabled = false; settings.MicrophoneEnabled = true; }); Silence();
            Check(controller.Status.AudioWarning.Length == 0 && recorder.ProcessId is null,
                "Microphone-only capture never receives a missing-game-audio warning");
            SeedSilence(); Settings(settings => { settings.AudioEnabled = true; settings.SystemGainPercent = 0; }); Silence();
            Check(controller.Status.AudioWarning.Length == 0 && Field("_silentSinceCounter").GetValue(controller) is null,
                "Intentionally muted system audio never receives a silence warning");

            Field("_recordStatus").SetValue(controller, new RecorderStatus(false, "", "Bereit", 0, 0, null));
            var update = await controller.CheckForUpdatesAsync();
            Check(handler.Calls == 1 && update.IsNewer && update.Version == "9.0.0",
                "Update checking reads pinned release metadata through the injected fake HTTP client");
            session.SetPrivacy(false, false, true); session.Start();
            try
            {
                controller.LaunchPreparedUpdate(new PreparedUpdate("9.0.0", folder, Path.Combine(folder, "never-launched.exe")));
                throw new InvalidOperationException("An armed session must block update launch.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Stoppe zuerst"))
            { Check(recorder.ProcessId is null, "Updates cannot launch while recording is armed, including a paused session"); }
            session.Stop();
            try { await controller.AddBookmarkAsync("should not be saved"); throw new InvalidOperationException("A stopped recording must reject bookmarks."); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("laufenden Aufnahme"))
            { Check(!Directory.EnumerateFiles(folder, "*.bookmarks.json").Any(), "Stopped recording rejects bookmarks without writing sidecars"); }

            if (recorder.ProcessId is not null || Directory.EnumerateFiles(folder, "*.mkv").Any())
                throw new InvalidOperationException("Feature policy checks unexpectedly started capture.");
            Console.WriteLine($"{passed} controller feature checks passed; no capture, real network, settings persistence, registry or installation used.");
            return 0;
        }
        finally
        {
            blockSnapshot = false; snapshotRelease.Set();
            Directory.Delete(folder, true);
        }
    }

    private static FieldInfo Field(string name) => typeof(AppController).GetField(name, Flags) ?? throw new MissingFieldException(name);
    private static MethodInfo Method(string name) => typeof(AppController).GetMethod(name, Flags) ?? throw new MissingMethodException(name);

    private sealed class FakeUpdateHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (request.RequestUri?.AbsoluteUri != "https://api.github.com/repos/sercan1115/ScreenDash/releases/latest")
                throw new InvalidOperationException("Unexpected update request in fake handler.");
            var data = JsonSerializer.Serialize(new
            {
                draft = false, prerelease = false, tag_name = "v9.0.0",
                html_url = "https://github.com/sercan1115/ScreenDash/releases/tag/v9.0.0",
                assets = new[]
                {
                    new { name = "ScreenDash-9.0.0-Windows.zip", state = "uploaded", size = 100L, browser_download_url = "https://github.com/sercan1115/ScreenDash/releases/download/v9.0.0/ScreenDash-9.0.0-Windows.zip" },
                    new { name = "ScreenDash-9.0.0-SHA256SUMS.txt", state = "uploaded", size = 100L, browser_download_url = "https://github.com/sercan1115/ScreenDash/releases/download/v9.0.0/ScreenDash-9.0.0-SHA256SUMS.txt" }
                }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") });
        }
    }
}
