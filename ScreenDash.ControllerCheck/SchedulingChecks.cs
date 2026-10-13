using System.Reflection;
using Microsoft.Win32;
using ScreenDash;

internal static class SchedulingChecks
{
    internal static async Task<int> RunAsync()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var folder = Path.Combine(Path.GetTempPath(), "ScreenDash-scheduling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        int checks = 0;
        void Check(bool condition, string description) { if (!condition) throw new InvalidOperationException(description); checks++; Console.WriteLine("PASS " + description); }
        try
        {
            using var controller = new AppController(new AppSettings { RecordingDirectory = folder, ShowNotifications = false });
            var limit = DateTime.UtcNow.AddSeconds(5);
            while (controller.Status.Busy && DateTime.UtcNow < limit) await Task.Delay(10);
            Check(!controller.Status.Busy && controller.Status.Storage is not null, "Initial storage check finishes in the background");
            var store = (RecordingStore)typeof(AppController).GetField("_store", flags)!.GetValue(controller)!;
            var storeGate = typeof(RecordingStore).GetField("_gate", flags)!.GetValue(store)!;
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            var blocker = Task.Run(() => { lock (storeGate) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Store block did not release."); } });
            entered.Wait(TimeSpan.FromSeconds(2));
            try
            {
                using var cancel = new CancellationTokenSource();
                var archive = controller.GetRecordingsAsync(cancel.Token); await Task.Delay(100);
                Check(!archive.IsCompleted, "Archive scan can be deliberately blocked without capturing");
                var refresh = (Task)typeof(AppController).GetMethod("RefreshStorageAsync", flags)!.Invoke(controller, [false])!;
                await controller.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
                Check(!archive.IsCompleted && !refresh.IsCompleted, "Stop does not wait for a blocked archive or background cleanup");
                cancel.Cancel();
                try { await archive.WaitAsync(TimeSpan.FromSeconds(1)); throw new InvalidOperationException("Expected cancellation."); }
                catch (OperationCanceledException) { Check(true, "Archive cancellation completes before the blocked directory releases"); }
            }
            finally { release.Set(); await blocker; }

            var gate = (SemaphoreSlim)typeof(AppController).GetField("_gate", flags)!.GetValue(controller)!;
            var session = (RecordingSessionState)typeof(AppController).GetField("_session", flags)!.GetValue(controller)!;
            using var startup = new CancellationTokenSource();
            await gate.WaitAsync();
            try
            {
                session.Start(); typeof(AppController).GetField("_pendingStart", flags)!.SetValue(controller, startup);
                typeof(AppController).GetMethod("OnSessionSwitch", flags)!.Invoke(controller, [controller, new SessionSwitchEventArgs(SessionSwitchReason.SessionLock)]);
                Check(session.Paused && !session.CanCapture && startup.IsCancellationRequested, "Lock immediately changes intent and cancels startup while the capture gate is busy");
                typeof(AppController).GetField("_pendingStart", flags)!.SetValue(controller, null);
                session.Stop();
                typeof(AppController).GetMethod("OnSessionSwitch", flags)!.Invoke(controller, [controller, new SessionSwitchEventArgs(SessionSwitchReason.SessionUnlock)]);
                Check(!session.Armed, "Unlock cannot resurrect a stopped session");
            }
            finally { gate.Release(); }

            controller.Dispose();
            using var pauseController = new AppController(new AppSettings { RecordingDirectory = folder, ShowNotifications = false });
            limit = DateTime.UtcNow.AddSeconds(5);
            while (pauseController.Status.Busy && DateTime.UtcNow < limit) await Task.Delay(10);
            gate = (SemaphoreSlim)typeof(AppController).GetField("_gate", flags)!.GetValue(pauseController)!;
            session = (RecordingSessionState)typeof(AppController).GetField("_session", flags)!.GetValue(pauseController)!;
            await gate.WaitAsync();
            Task oldPause;
            try
            {
                session.Start(); oldPause = pauseController.PauseAsync();
                session.Stop(); session.Start();
            }
            finally { gate.Release(); }
            await oldPause.WaitAsync(TimeSpan.FromSeconds(1));
            Check(session.Armed && session.CanCapture, "A superseded pause request cannot stop a new session");
            await pauseController.StopAsync();
            Console.WriteLine($"{checks} controller scheduling checks passed; no screen or audio recorded.");
            return 0;
        }
        finally { Directory.Delete(folder, true); }
    }
}
