using ScreenDash;

var checks = new (string Name, Action Run)[]
{
    ("New session stays stopped until the user starts", () =>
    {
        var state = new RecordingSessionState();
        Require(!state.Armed && !state.Paused && !state.CanCapture, "Initial state must not capture.");
        Require(!state.CanRecover(state.Generation), "A stopped app must not recover.");
        state.Start();
        Require(state.Armed && state.CanCapture && !state.Paused, "Explicit start must enable capture.");
    }),
    ("A queued recovery cannot resurrect a stopped recording", () =>
    {
        var state = Started();
        var pendingRecovery = state.Generation;
        Require(state.CanRecover(pendingRecovery), "An active session can recover.");
        state.Stop();
        state.SetPrivacy(true, false, true);
        state.SetPrivacy(false, false, true);
        Require(!state.CanRecover(pendingRecovery) && !state.Armed, "Unlock must not restart after Stop.");
    }),
    ("Old recovery cannot interrupt a new session after Stop and Start", () =>
    {
        var state = Started();
        var oldRecovery = state.Generation;
        state.Stop();
        state.Start();
        Require(state.CanCapture && state.CanRecover(state.Generation), "The new recording must remain enabled.");
        Require(!state.CanRecover(oldRecovery), "The old delayed recovery must not stop or restart the new recording.");
    }),
    ("Manual pause survives PC lock, sleep, wake, and unlock", () =>
    {
        var state = Started();
        state.Pause();
        var manualRecovery = state.Generation;
        state.SetPrivacy(true, true, true);
        state.SetPrivacy(true, false, true);
        state.SetPrivacy(false, false, true);
        Require(state.Armed && state.ManualPaused && state.Paused && !state.CanCapture,
            "Privacy events must preserve the manual pause.");
        Require(!state.CanRecover(manualRecovery), "A manually paused recording must never auto-recover.");
        state.Resume();
        Require(state.CanCapture && !state.ManualPaused, "Only explicit Resume must release the manual pause.");
    }),
    ("Resume while the PC is locked still blocks capture", () =>
    {
        var state = Started();
        state.Pause();
        state.SetPrivacy(true, false, true);
        state.Resume();
        Require(state.Armed && !state.ManualPaused && state.Paused && !state.CanCapture,
            "Resume must not override the privacy lock.");
        state.SetPrivacy(false, false, true);
        Require(state.CanCapture && !state.Paused, "Unlock may resume when the manual pause has been released.");
    }),
    ("Repeated Resume during healthy capture leaves the session untouched", () =>
    {
        var state = Started();
        var activeGeneration = state.Generation;
        state.Resume();
        state.Resume();
        state.Resume();
        Require(state.Armed && state.CanCapture && !state.Paused && !state.ManualPaused,
            "Repeated Resume must leave an already healthy capture running.");
        Require(state.Generation == activeGeneration && state.CanRecover(activeGeneration),
            "An unnecessary Resume must not cancel valid work or create a new recording generation.");
    }),
    ("Suspend blocks capture even when pause on lock is disabled", () =>
    {
        var state = Started();
        state.SetPrivacy(true, false, false);
        Require(state.CanCapture, "Disabling lock pause should preserve an existing recording.");
        state.SetPrivacy(true, true, false);
        Require(state.Paused && !state.CanCapture, "Sleep must always suspend capture.");
    }),
    ("Starting while locked or asleep is rejected", () =>
    {
        var state = new RecordingSessionState();
        state.SetPrivacy(true, false, false);
        Reject<InvalidOperationException>(state.Start);
        state.SetPrivacy(false, true, true);
        Reject<InvalidOperationException>(state.Start);
        Require(!state.Armed, "Rejected Start must not arm a recording.");
    }),
    ("Stopping a paused recording clears the pause", () =>
    {
        var state = Started();
        state.Pause();
        state.Stop();
        Require(!state.Armed && !state.ManualPaused && !state.Paused && state.PauseReason.Length == 0,
            "Stopped sessions must display stopped rather than paused.");
        state.Start();
        Require(state.CanCapture, "A new explicit start must work after a paused session was stopped.");
    }),
    ("Duplicate privacy notifications preserve valid recovery", () =>
    {
        var state = Started();
        var pendingRecovery = state.Generation;
        state.SetPrivacy(false, false, true);
        state.SetPrivacy(false, false, true);
        Require(state.CanRecover(pendingRecovery), "Unchanged notifications must not cancel an active recovery.");
        state.SetPrivacy(true, false, true);
        state.SetPrivacy(false, false, true);
        Require(!state.CanRecover(pendingRecovery), "A real privacy transition must invalidate the old recovery.");
    }),
    ("Pause and Resume cannot arm an idle recording", () =>
    {
        var state = new RecordingSessionState();
        Reject<InvalidOperationException>(state.Pause);
        Reject<InvalidOperationException>(state.Resume);
        Require(!state.Armed && !state.CanCapture, "Pause and Resume must not start a stopped recording.");
    }),
    ("Shutdown cannot be undone by privacy or pending recovery", () =>
    {
        var state = Started();
        var pendingRecovery = state.Generation;
        state.Shutdown();
        state.SetPrivacy(true, true, true);
        state.SetPrivacy(false, false, true);
        Require(!state.Armed && !state.CanCapture && !state.CanRecover(pendingRecovery),
            "Shutdown must prevent all automatic restarts.");
        Reject<InvalidOperationException>(state.Start);
        Reject<InvalidOperationException>(state.Resume);
    }),
    ("Repeated Stop still invalidates all previous generations", () =>
    {
        var state = Started();
        state.Stop();
        var afterFirstStop = state.Generation;
        state.Stop();
        Require(state.Generation > afterFirstStop && !state.CanCapture, "Every user Stop must invalidate delayed work.");
    }),
    ("Storage estimate uses decimal bitrate and binary GiB", () =>
    {
        var estimate = RecordingEstimates.Calculate(EstimateSettings());
        Near(3_708_000_000, estimate.BytesPerHour, 0.001, "8 Mbps should use 3.708 GB per hour including muxing.");
        Near(3_708_000_000d / GiB, estimate.GiBPerHour, 1e-9, "GiB must use 1024 cubed.");
    }),
    ("System sound adds the configured AAC storage overhead", () =>
    {
        var settings = EstimateSettings();
        var videoOnly = RecordingEstimates.Calculate(settings);
        settings.AudioEnabled = true;
        var withAudio = RecordingEstimates.Calculate(settings);
        Near(59_328_000, withAudio.BytesPerHour - videoOnly.BytesPerHour, 0.001, "128 kbps AAC plus 3% margin must be counted.");
        Require(withAudio.HoursAtLimit < videoOnly.HoursAtLimit, "Audio should shorten capacity at the same quota.");
    }),
    ("Protected recordings consume rolling recording capacity", () =>
    {
        var settings = EstimateSettings();
        settings.MaxStorageGb = 100;
        var baseline = RecordingEstimates.Calculate(settings);
        var halfProtected = RecordingEstimates.Calculate(settings, 50 * GiBBytes);
        Near(baseline.HoursAtLimit / 2, halfProtected.HoursAtLimit, 1e-9, "50 GiB protected out of 100 GiB leaves half the quota.");
        Near(baseline.GiBForFullRetention + 50, halfProtected.GiBForFullRetention, 1e-9, "Full retention must account for protected storage.");
    }),
    ("A quota filled by protected recordings has zero rolling capacity", () =>
    {
        var settings = EstimateSettings();
        settings.MaxStorageGb = 1;
        var estimate = RecordingEstimates.Calculate(settings, 2 * GiBBytes);
        Require(estimate.HoursAtLimit == 0 && estimate.EffectiveRetentionHours == 0 && !estimate.FullRetentionFits,
            "Protected files exceeding quota must not yield negative or usable capacity.");
    }),
    ("Free disk space respects the reserve", () =>
    {
        var settings = EstimateSettings();
        settings.MaxStorageGb = 4096;
        settings.MinFreeSpaceGb = 2;
        var full = RecordingEstimates.Calculate(settings);
        var constrained = RecordingEstimates.Calculate(settings, 0, 3 * GiBBytes);
        Near(GiB / full.BytesPerHour, constrained.EffectiveRetentionHours, 1e-9,
            "3 GiB available minus a 2 GiB reserve leaves only 1 GiB for recording.");
        var exhausted = RecordingEstimates.Calculate(settings, 0, GiBBytes);
        Require(exhausted.EffectiveRetentionHours == 0 && !exhausted.FullRetentionFits,
            "Free space below reserve must report no additional recording capacity.");
    }),
    ("Retention policy caps usable history even with abundant disk", () =>
    {
        var settings = EstimateSettings();
        settings.VideoBitrateKbps = 500;
        settings.MaxStorageGb = 4096;
        settings.RetentionDays = 7;
        var estimate = RecordingEstimates.Calculate(settings);
        Near(168, estimate.EffectiveRetentionHours, 1e-9, "Seven days must cap retained history at 168 hours.");
        Require(estimate.FullRetentionFits, "Enough quota should allow the complete retention window.");
    }),
    ("Estimate clamps invalid persisted numbers without mutating settings", () =>
    {
        var invalid = EstimateSettings();
        invalid.VideoBitrateKbps = 0;
        invalid.RetentionDays = 0;
        invalid.MaxStorageGb = 0;
        invalid.MinFreeSpaceGb = 0;
        var sane = EstimateSettings();
        sane.VideoBitrateKbps = 500;
        sane.RetentionDays = 1;
        sane.MaxStorageGb = 1;
        sane.MinFreeSpaceGb = 1;
        var actual = RecordingEstimates.Calculate(invalid, -100, 2 * GiBBytes);
        var expected = RecordingEstimates.Calculate(sane, 0, 2 * GiBBytes);
        Require(actual == expected, "Out-of-range input should use supported numeric bounds.");
        Require(invalid.VideoBitrateKbps == 0 && invalid.RetentionDays == 0 && invalid.MaxStorageGb == 0,
            "Planning must not modify saved settings.");
        Reject<ArgumentNullException>(() => RecordingEstimates.Calculate(null!));
    }),
    ("Recording duration excludes pauses and continues across resume", () =>
    {
        long counter = 0;
        var clock = new RecordingClock(() => counter, 1000);
        clock.Begin(); counter = 3500;
        Near(3.5, clock.Elapsed.TotalSeconds, 1e-9, "Recording must advance.");
        clock.End(); counter = 99_000;
        Near(3.5, clock.Elapsed.TotalSeconds, 1e-9, "A pause must not add time.");
        clock.Begin(); counter = 101_000; clock.End();
        Near(5.5, clock.Elapsed.TotalSeconds, 1e-9, "Resume must keep the previous duration.");
    }),
    ("Timer is disabled by default and clears after stop", () =>
    {
        Require(RecordingTimer.Remaining(0, TimeSpan.FromHours(100), true) is null && !RecordingTimer.HasExpired(0, TimeSpan.FromHours(100)), "No timer means unlimited capture time.");
        Require(RecordingTimer.Remaining(60, TimeSpan.FromMinutes(5), false) is null, "A stopped session has no countdown.");
        Require(new AppSettings().AutoStopMinutes == 0, "Existing users must not unexpectedly receive a time limit.");
    }),
    ("Timer stops exactly at the active duration and does not count paused time", () =>
    {
        long counter = 0; var clock = new RecordingClock(() => counter, 1000);
        clock.Begin(); counter = 30_000; clock.End(); counter = 900_000;
        Require(RecordingTimer.Remaining(1, clock.Elapsed, true) == TimeSpan.FromSeconds(30), "Paused wall time must not consume the countdown.");
        clock.Begin(); counter += 29_999;
        Require(!RecordingTimer.HasExpired(1, clock.Elapsed), "A timer must not fire before its boundary.");
        counter++;
        Require(RecordingTimer.HasExpired(1, clock.Elapsed) && RecordingTimer.Remaining(1, clock.Elapsed, true) == TimeSpan.Zero, "The boundary must stop and show zero.");
    }),
    ("Timer clamps excessive limits and never shows a negative countdown", () =>
    {
        Require(RecordingTimer.Remaining(int.MaxValue, TimeSpan.Zero, true) == TimeSpan.FromDays(1), "Maximum is one day of active capture.");
        Require(RecordingTimer.Remaining(1, TimeSpan.FromDays(30), true) == TimeSpan.Zero, "Expired countdown must stay at zero.");
        Require(RecordingTimer.Remaining(1, TimeSpan.FromSeconds(-5), true) == TimeSpan.FromMinutes(1), "Invalid negative elapsed time must not extend the timer.");
    }),
    ("Repeated clock events do not double count and reset starts fresh", () =>
    {
        long counter = 0;
        var clock = new RecordingClock(() => counter, 1000);
        clock.Begin(); counter = 500; clock.Begin(); counter = 1500;
        clock.End(); clock.End();
        Near(1.5, clock.Elapsed.TotalSeconds, 1e-9, "Repeated device/status events must be harmless.");
        clock.Reset(); counter = 9999; clock.Begin(); counter += 250;
        Near(0.25, clock.Elapsed.TotalSeconds, 1e-9, "A new session must start at zero.");
        Reject<ArgumentOutOfRangeException>(() => new RecordingClock(() => 0, 0));
    })
};

var failed = 0;
foreach (var check in checks)
{
    try { check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + check.Name + ": " + error.Message); }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} lifecycle and planning checks passed.");
return failed == 0 ? 0 : 1;

static RecordingSessionState Started()
{
    var state = new RecordingSessionState();
    state.Start();
    return state;
}

static AppSettings EstimateSettings() => new() { VideoBitrateKbps = 8_000, AudioEnabled = false, MaxStorageGb = 50, RetentionDays = 7 };
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Near(double expected, double actual, double tolerance, string message)
{
    Require(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance, $"{message} Expected {expected}; got {actual}.");
}
static void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
}

// Named values prevent decimal GB and binary GiB from silently being conflated.
partial class Program
{
    private const long GiBBytes = 1_073_741_824;
    private const double GiB = 1_073_741_824d;
}
