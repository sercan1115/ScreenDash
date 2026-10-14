using ScreenDash;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

var tests = new (string Name, Action Run)[]
{
    ("seven-day retention and exact cutoff", Retention),
    ("protected recordings survive retention", ProtectedRetention),
    ("quota evicts oldest recordings", OldestQuota),
    ("protected quota blocks new recordings", ProtectedQuota),
    ("free-space reserve and check-interval headroom", FreeSpaceReserve),
    ("only latest active segment is excluded", ActiveSegment),
    ("segment birth time distinguishes long sessions", SegmentStartTimes),
    ("open recordings are never deleted", OpenSegment),
    ("recent overlap and active segment protection", RecentProtection),
    ("unrelated files and path escapes are preserved", OwnershipBoundary),
    ("directory junction is not traversed", JunctionBoundary),
    ("settings clamp a clone and reject unsafe choices", SettingsValidation),
    ("settings atomic roundtrip and corrupt recovery", SettingsRoundtrip),
    ("Unicode labels persist and trim safely", LabelRoundtrip),
    ("labels reject unsafe paths and invalid text", LabelValidation),
    ("corrupt and oversized metadata is safely ignored", LabelCorruption),
    ("cleanup removes owned notes after the video only", CleanupSidecars),
    ("manual batch deletion validates every target first", ManualDeleteBoundary),
    ("manual deletion requires protected confirmation", ManualDeleteProtected),
    ("manual deletion never removes latest active segment", ManualDeleteActive),
    ("open file blocks entire manual deletion batch", ManualDeleteOpen),
    ("read-only file blocks entire manual deletion batch", ManualDeleteReadOnly),
    ("manual deletion removes only matching owned sidecars", ManualDeleteSidecars),
    ("metadata junction is never followed or removed", MetadataJunction)
    ,("unchanged notes are cached and external replacements refresh", LabelCache)
    ,("missing notes and videos invalidate cached labels", LabelCacheRemoval)
    ,("invalid notes recover after external changes and locks", InvalidLabelCache)
    ,("missing recordings cannot create orphan protection markers", MissingProtection)
    ,("hidden marker directories keep recordings protected", HiddenMarker)
    ,("large manual batch preserves notes until all targets validate", LargeManualBatch)
    ,("canceled list preserves recordings and metadata", CanceledList)
    ,("canceled list does not wait for another store operation", CanceledListWaitingForStore)
}.Concat(BookmarkChecks.All).ToArray();
var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static DateTime Now() => new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
static AppSettings Settings(string root) => new() { RecordingDirectory = root, RetentionDays = 7, MaxStorageGb = 50, MinFreeSpaceGb = 2 };
static RecordingStore Store(string root) => new(root, _ => 200L * 1024 * 1024 * 1024);
static string Prefix(DateTime date, string id = "1234abcd") => "screendash_" + date.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "_" + id;
static string Segment(string root, DateTime start, DateTime completed, int number = 0, long bytes = 128, string id = "1234abcd")
{
    var path = Path.Combine(root, Prefix(start, id) + "_" + number.ToString("D6", CultureInfo.InvariantCulture) + ".mkv");
    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
    {
        if (bytes > 1024 * 1024 && OperatingSystem.IsWindows())
        {
            if (!NativeMethods.DeviceIoControl(stream.SafeFileHandle, 0x000900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException("Sparse test file could not be created: " + Marshal.GetLastWin32Error());
        }
        stream.SetLength(bytes);
    }
    File.SetLastWriteTimeUtc(path, completed);
    return path;
}
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}

static void Retention()
{
    using var temp = new Scratch();
    var now = Now();
    var old = Segment(temp.Root, now.AddDays(-8), now.AddDays(-7).AddMinutes(-1));
    var boundary = Segment(temp.Root, now.AddDays(-8), now.AddDays(-7), id: "5678abcd");
    var recent = Segment(temp.Root, now.AddDays(-2), now.AddDays(-1));
    var result = Store(temp.Root).Cleanup(Settings(temp.Root), nowUtc: now);
    Check(result.DeletedCount == 1 && result.DeletedBytes == 128, "Expected only the expired segment to be removed.");
    Check(!File.Exists(old) && File.Exists(boundary) && File.Exists(recent), "Retention must use completed UTC time and preserve exact cutoff.");
    Check(result.CanRecord && result.TotalBytes == 256, "Remaining bytes should be counted.");
}

static void ProtectedRetention()
{
    using var temp = new Scratch();
    var now = Now();
    var protectedPath = Segment(temp.Root, now.AddDays(-10), now.AddDays(-9));
    var store = Store(temp.Root);
    store.SetProtected(protectedPath, true);
    var result = store.Cleanup(Settings(temp.Root), nowUtc: now);
    Check(result.DeletedCount == 0 && result.ProtectedBytes == 128 && File.Exists(protectedPath), "Protected old footage must survive.");
    Check(store.List().Single().Protected, "Protection should persist through its sidecar.");
    store.SetProtected(protectedPath, false);
    Check(!File.Exists(protectedPath + ".keep"), "Unprotect must remove its sidecar.");
    Check(store.Cleanup(Settings(temp.Root), nowUtc: now).DeletedCount == 1, "Unprotected expired footage should now be deleted.");
}

static void OldestQuota()
{
    using var temp = new Scratch();
    var now = Now();
    const long bytes = 384L * 1024 * 1024;
    var oldest = Segment(temp.Root, now.AddDays(-3), now.AddDays(-3), bytes: bytes);
    var middle = Segment(temp.Root, now.AddDays(-2), now.AddDays(-2), bytes: bytes);
    var newest = Segment(temp.Root, now.AddDays(-1), now.AddDays(-1), bytes: bytes);
    var settings = Settings(temp.Root);
    settings.MaxStorageGb = 1;
    var result = Store(temp.Root).Cleanup(settings, nowUtc: now);
    Check(result.DeletedCount == 1 && !File.Exists(oldest), "Quota should evict oldest first.");
    Check(File.Exists(middle) && File.Exists(newest) && result.CanRecord, "Enough newer footage and headroom should remain.");
    Check(result.TotalBytes == 2 * bytes, "Quota includes all remaining owned segments.");
}

static void ProtectedQuota()
{
    using var temp = new Scratch();
    var now = Now();
    var protectedPath = Segment(temp.Root, now.AddDays(-3), now.AddDays(-3), bytes: 1024L * 1024 * 1024);
    var removable = Segment(temp.Root, now.AddDays(-1), now.AddDays(-1), bytes: 128 * 1024);
    var store = Store(temp.Root);
    store.SetProtected(protectedPath, true);
    var settings = Settings(temp.Root);
    settings.MaxStorageGb = 1;
    var result = store.Cleanup(settings, nowUtc: now);
    Check(!result.CanRecord && result.ProtectedBytes == 1024L * 1024 * 1024, "Protected footage at quota must block recording.");
    Check(File.Exists(protectedPath) && !File.Exists(removable), "Protected quota may only reclaim unprotected files.");
}

static void FreeSpaceReserve()
{
    using var temp = new Scratch();
    var settings = Settings(temp.Root);
    const long reserve = 2L * 1024 * 1024 * 1024;
    Check(!new RecordingStore(temp.Root, _ => reserve).Cleanup(settings, nowUtc: Now()).CanRecord, "The reserve itself cannot be consumed.");
    Check(!new RecordingStore(temp.Root, _ => reserve + 1024 * 1024).Cleanup(settings, nowUtc: Now()).CanRecord, "A small positive margin is insufficient for the next check interval.");
    Check(new RecordingStore(temp.Root, _ => reserve + 64L * 1024 * 1024).Cleanup(settings, nowUtc: Now()).CanRecord, "Safe free-space headroom should allow recording.");
}

static void ActiveSegment()
{
    using var temp = new Scratch();
    var now = Now();
    var started = now.AddDays(-9);
    var closed = Segment(temp.Root, started, now.AddDays(-8), 0);
    var active = Segment(temp.Root, started, now.AddDays(-8), 1);
    var store = Store(temp.Root);
    var prefix = Prefix(started);
    Check(store.List(prefix).Count(e => e.Active) == 1 && store.List(prefix).Single(e => e.Active).Path == active, "Only last fixed-width segment number is active.");
    var result = store.Cleanup(Settings(temp.Root), prefix, now);
    Check(result.DeletedCount == 1 && !File.Exists(closed) && File.Exists(active), "Closed older segments from the same active session remain eligible.");
}

static void OpenSegment()
{
    using var temp = new Scratch();
    var now = Now();
    var path = Segment(temp.Root, now.AddDays(-10), now.AddDays(-9));
    var store = Store(temp.Root);
    using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
    {
        Check(store.Cleanup(Settings(temp.Root), nowUtc: now).DeletedCount == 0 && File.Exists(path), "An open file must remain untouched.");
    }
    Check(store.Cleanup(Settings(temp.Root), nowUtc: now).DeletedCount == 1, "The closed expired segment should be reclaimable.");
}

static void SegmentStartTimes()
{
    using var temp = new Scratch();
    var now = Now();
    var sessionStart = now.AddHours(-1);
    var first = Segment(temp.Root, sessionStart, now.AddMinutes(-50), 0);
    var second = Segment(temp.Root, sessionStart, now.AddMinutes(-40), 1);
    File.SetCreationTimeUtc(first, sessionStart);
    File.SetCreationTimeUtc(second, now.AddMinutes(-50));
    var store = Store(temp.Root);
    var actualSecond = store.List().Single(e => e.Path == second);
    Check(actualSecond.StartedUtc == now.AddMinutes(-50), "A later segment should display its own bounded creation timestamp.");
    File.SetCreationTimeUtc(second, now.AddDays(1));
    Check(store.List().Single(e => e.Path == second).StartedUtc == sessionStart, "An impossible copied-file birth timestamp should fall back to session UTC.");
}

static void RecentProtection()
{
    using var temp = new Scratch();
    var now = Now();
    var start = now.AddHours(-1);
    var older = Segment(temp.Root, start, now.AddMinutes(-11), 0);
    var overlap = Segment(temp.Root, start, now.AddMinutes(-9), 1);
    var active = Segment(temp.Root, start, now.AddMinutes(-30), 2, bytes: 0);
    var store = Store(temp.Root);
    using var open = new FileStream(active, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
    Check(store.ProtectRecent(10, Prefix(start), now) == 2, "Recent completed footage and the empty/open active segment should be protected.");
    Check(!File.Exists(older + ".keep") && File.Exists(overlap + ".keep") && File.Exists(active + ".keep"), "Protection should cover overlap rather than just session start.");
    Check(store.ProtectRecent(10, Prefix(start), now) == 0, "Repeated protection should be idempotent.");
}

static void OwnershipBoundary()
{
    using var temp = new Scratch();
    var now = Now();
    var unrelated = Path.Combine(temp.Root, "holiday.mkv");
    var malformed = Path.Combine(temp.Root, "screendash_20269999T123456000Z_1234abcd_000000.mkv");
    File.WriteAllText(unrelated, "private video");
    File.WriteAllText(malformed, "invalid date is not owned");
    var nested = Directory.CreateDirectory(Path.Combine(temp.Root, "nested")).FullName;
    var nestedRecording = Segment(nested, now.AddDays(-10), now.AddDays(-9));
    var store = Store(temp.Root);
    Throws<ArgumentException>(() => store.SetProtected(unrelated, true));
    Throws<ArgumentException>(() => store.SetProtected(nestedRecording, true));
    Throws<ArgumentException>(() => store.SetProtected(Path.Combine(temp.Root, "..", Path.GetFileName(nestedRecording)), true));
    Check(store.List().Count == 0, "Only direct children with valid ScreenDash names should be listed.");
    store.Cleanup(Settings(temp.Root), nowUtc: now);
    Check(File.Exists(unrelated) && File.Exists(malformed) && File.Exists(nestedRecording), "Cleanup must preserve unrelated names and nested files.");
}

static void JunctionBoundary()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new Scratch();
    using var external = new Scratch();
    var outside = Segment(external.Root, Now().AddDays(-10), Now().AddDays(-9));
    var link = Path.Combine(temp.Root, "linked");
    var process = Process.Start(new ProcessStartInfo("cmd.exe")
    {
        Arguments = $"/c mklink /J \"{link}\" \"{external.Root}\"",
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
    }) ?? throw new InvalidOperationException("Junction helper did not start.");
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException("Junction creation failed: " + process.StandardError.ReadToEnd());
    try
    {
        Throws<IOException>(() => new RecordingStore(link));
        Store(temp.Root).Cleanup(Settings(temp.Root), nowUtc: Now());
        Check(File.Exists(outside), "Cleanup must not traverse junctions into another directory.");
    }
    finally { Directory.Delete(link); }
}

static void SettingsValidation()
{
    using var temp = new Scratch();
    var input = Settings(temp.Root);
    input.RetentionDays = 0;
    input.Fps = 999;
    input.VideoBitrateKbps = -2;
    input.MinFreeSpaceGb = 0;
    input.AudioDeviceId = null!;
    var validated = SettingsStore.Validate(input);
    Check(validated.RetentionDays == 1 && validated.Fps == 60 && validated.VideoBitrateKbps == 500 && validated.MinFreeSpaceGb == 1, "Settings outside supported ranges should be clamped.");
    Check(input.RetentionDays == 0 && input.Fps == 999 && validated != input, "Validation must clone rather than mutate UI state.");
    Check(validated.AudioDeviceId == "", "Missing device identifiers should be normalized.");
    input.Fps = 1;
    input.ShowNotifications = false;
    input.MinimizeOnRecord = true;
    var preferences = SettingsStore.Validate(input);
    Check(preferences.Fps == 5 && !preferences.ShowNotifications && preferences.MinimizeOnRecord, "The lower FPS bound and boolean preferences should survive cloned validation.");
    input.EncoderMode = "Unknown";
    Throws<ArgumentException>(() => SettingsStore.Validate(input));
    input.EncoderMode = "Auto";
    input.RecordingDirectory = "relative/path";
    Throws<ArgumentException>(() => SettingsStore.Validate(input));
    input.RecordingDirectory = @"\\server\share\video";
    Throws<ArgumentException>(() => SettingsStore.Validate(input));
}

static void SettingsRoundtrip()
{
    using var temp = new Scratch();
    var file = Path.Combine(temp.Root, "settings.json");
    var input = Settings(temp.Root);
    input.EncoderMode = "NVIDIA";
    input.RecordOnLaunch = true;
    input.ShowNotifications = false;
    input.MinimizeOnRecord = true;
    input.Fps = 27;
    input.AnimatedBackground = false;
    SettingsStore.Save(input, file);
    var restored = SettingsStore.Load(file);
    Check(restored.RecordingDirectory == input.RecordingDirectory && restored.EncoderMode == "NVIDIA" && restored.RecordOnLaunch, "Saved choices should survive roundtrip.");
    Check(!restored.ShowNotifications && restored.MinimizeOnRecord && restored.Fps == 27 && !restored.AnimatedBackground, "Notification/minimize/motion preferences and custom FPS must survive validation and roundtrip.");
    input.RetentionDays = 9;
    SettingsStore.Save(input, file);
    Check(SettingsStore.Load(file).RetentionDays == 9 && !Directory.EnumerateFiles(temp.Root, "*.tmp").Any(), "Replacement should finish atomically without leftover temporary files.");
    File.WriteAllText(file, "{corrupt:json");
    var defaults = SettingsStore.Load(file);
    Check(defaults.RetentionDays == 7 && !File.Exists(file), "Corruption should recover default settings.");
    var backup = Directory.EnumerateFiles(temp.Root, "settings.json.corrupt-*").Single();
    Check(File.ReadAllText(backup) == "{corrupt:json", "The corrupt original must remain recoverable.");
    File.WriteAllText(file, "{\"RecordingDirectory\":\"" + temp.Root.Replace("\\", "\\\\") + "\"}");
    var migrated = SettingsStore.Load(file);
    Check(migrated.ShowNotifications && !migrated.MinimizeOnRecord && migrated.AnimatedBackground, "Existing settings without new preferences should keep their natural defaults.");
}

static void LabelRoundtrip()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var store = Store(temp.Root);
    store.SetLabel(path, "  Überfahrt München 🚗 – Beweis  ");
    Check(Store(temp.Root).List().Single().Label == "Überfahrt München 🚗 – Beweis", "Labels should be trimmed and survive a fresh store instance with Unicode intact.");
    store.SetLabel(path, string.Concat(Enumerable.Repeat("🚗", 120)));
    Check(Store(temp.Root).List().Single().Label.EnumerateRunes().Count() == 120, "120 Unicode scalars should fit even with surrogate pairs.");
    store.SetLabel(path, "   ");
    Check(!File.Exists(path + ".note.json") && Store(temp.Root).List().Single().Label == "", "Clearing a label should remove only its note metadata.");
    Check(!Directory.EnumerateFiles(temp.Root, "*.tmp").Any(), "Metadata writes should leave no temporary files.");
}

static void LabelValidation()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var store = Store(temp.Root);
    store.SetLabel(path, "Behalten");
    Throws<ArgumentException>(() => store.SetLabel(path, new string('x', 121)));
    Throws<ArgumentException>(() => store.SetLabel(path, "\nBeweis"));
    Throws<ArgumentException>(() => store.SetLabel(path, "Beweis\t"));
    Throws<ArgumentException>(() => store.SetLabel(path, "\ud800"));
    var unrelated = Path.Combine(temp.Root, "holiday.mkv");
    File.WriteAllText(unrelated, "private");
    Throws<ArgumentException>(() => store.SetLabel(unrelated, "fremd"));
    Throws<ArgumentException>(() => store.SetLabel(Path.Combine(temp.Root, "..", Path.GetFileName(path)), "außerhalb"));
    var missing = Path.Combine(temp.Root, Prefix(Now(), "8765abcd") + "_000000.mkv");
    Throws<FileNotFoundException>(() => store.SetLabel(missing, "fehlend"));
    Check(store.List().Single().Label == "Behalten" && !File.Exists(unrelated + ".note.json"), "Rejected edits must preserve existing labels and unrelated files.");
}

static void LabelCorruption()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var note = path + ".note.json";
    var store = Store(temp.Root);
    foreach (var invalid in new[] { "{corrupt", "null", "{\"Label\":null}", "{\"Label\":\"bad\\nlabel\"}", "{\"Label\":\"" + new string('x', 121) + "\"}", new string('x', 4097) })
    {
        File.WriteAllText(note, invalid);
        Check(store.List().Single().Label == "", "Malformed, unsafe or oversized note metadata must be ignored.");
        Check(File.Exists(path) && File.ReadAllText(note) == invalid, "Reading malformed metadata must not alter it or its recording.");
    }
    store.SetLabel(path, "Repariert");
    Check(store.List().Single().Label == "Repariert", "A valid edit should recover from corrupt metadata.");
    using var open = new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    Check(Store(temp.Root).List().Single().Label == "", "An uncached note opened exclusively by another program should be safely ignored.");
}

static void LabelCache()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var note = path + ".note.json";
    var store = Store(temp.Root);
    store.SetLabel(path, "Erste Notiz");
    Check(store.List().Single().Label == "Erste Notiz", "First scan should read persisted metadata.");
    using (var open = new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Check(store.List().Single().Label == "Erste Notiz", "An unchanged cached note should be displayed without opening the metadata again.");
    var previousWrite = File.GetLastWriteTimeUtc(note);
    Store(temp.Root).SetLabel(path, "Andere Notiz");
    File.SetLastWriteTimeUtc(note, previousWrite.AddSeconds(1));
    Check(store.List().Single().Label == "Andere Notiz", "A label changed by another store must replace the cache.");
    store.SetLabel(path, "Eigene Änderung");
    Check(store.List().Single().Label == "Eigene Änderung", "An edit by this store must immediately invalidate its cache.");
}

static void LabelCacheRemoval()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var note = path + ".note.json";
    var store = Store(temp.Root);
    store.SetLabel(path, "Alt");
    Check(store.List().Single().Label == "Alt", "The initial note should be cached.");
    File.Delete(note);
    Check(store.List().Single().Label == "", "A removed note must not leave stale display text.");
    store.SetLabel(path, "Wieder da");
    Check(store.List().Single().Label == "Wieder da", "A recreated note should be loaded normally.");
    File.Delete(path);
    Check(store.List().Count == 0, "A removed video must not remain in the archive snapshot.");
    File.Delete(note);
    Segment(temp.Root, Now().AddHours(-1), Now());
    Check(store.List().Single().Label == "", "Reusing a removed recording name must not restore its former label.");
}

static void InvalidLabelCache()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var note = path + ".note.json";
    var store = Store(temp.Root);
    File.WriteAllText(note, "{broken");
    Check(store.List().Single().Label == "", "Invalid JSON should have an empty label.");
    var stamp = File.GetLastWriteTimeUtc(note);
    File.WriteAllText(note, "{\"Label\":\"Neu\"}");
    File.SetLastWriteTimeUtc(note, stamp.AddSeconds(1));
    using (var open = new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Check(store.List().Single().Label == "", "A changed but locked note must not return a prior cached value.");
    Check(store.List().Single().Label == "Neu", "Transient lock failures must recover even when metadata timestamps stay unchanged.");
    File.WriteAllText(note, "{\"Label\":\"bad\\nlabel\"}");
    File.SetLastWriteTimeUtc(note, stamp.AddSeconds(2));
    Check(store.List().Single().Label == "", "A valid cached note replaced by invalid text must be dropped.");
}

static void MissingProtection()
{
    using var temp = new Scratch();
    var path = Path.Combine(temp.Root, Prefix(Now()) + "_000000.mkv");
    var store = Store(temp.Root);
    Throws<FileNotFoundException>(() => store.SetProtected(path, true));
    Throws<FileNotFoundException>(() => store.SetProtected(path, false));
    Check(!File.Exists(path + ".keep"), "A missing recording must never create an orphan protection marker.");
}

static void HiddenMarker()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddDays(-10), Now().AddDays(-9));
    var marker = path + ".keep";
    Directory.CreateDirectory(marker);
    File.SetAttributes(marker, File.GetAttributes(marker) | FileAttributes.Hidden | FileAttributes.System);
    try
    {
        var store = Store(temp.Root);
        Check(store.List().Single().Protected, "Hidden/system marker directories must remain fail-safe protection indicators.");
        Check(store.Cleanup(Settings(temp.Root), nowUtc: Now()).DeletedCount == 0 && File.Exists(path), "Retention must preserve files with any marker, including a hidden directory.");
    }
    finally { File.SetAttributes(marker, FileAttributes.Directory); }
}

static void LargeManualBatch()
{
    using var temp = new Scratch();
    var store = Store(temp.Root);
    var paths = Enumerable.Range(0, 160).Select(i => Segment(temp.Root, Now().AddHours(-2), Now().AddHours(-1), i)).ToArray();
    foreach (var path in paths) store.SetLabel(path, "Auswahl");
    var marked = paths[^1];
    store.SetProtected(marked, true);
    Throws<IOException>(() => store.Delete(paths, false));
    Check(paths.All(File.Exists) && paths.All(p => File.Exists(p + ".note.json")), "A protected final target must preserve every recording and note in a large batch.");
    store.SetProtected(marked, false);
    Check(store.Delete(paths, false) == paths.Length, "The entire validated batch should be deleted after the protection is removed.");
    Check(paths.All(p => !File.Exists(p) && !File.Exists(p + ".note.json")), "Successful batch deletion should remove each recording's owned note.");
}

static void CanceledList()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var store = Store(temp.Root);
    store.SetLabel(path, "Behalten 🚗");
    store.SetProtected(path, true);
    var note = File.ReadAllBytes(path + ".note.json");
    var marker = File.ReadAllBytes(path + ".keep");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        store.List(cancellationToken: cancellation.Token);
        throw new InvalidOperationException("A canceled refresh must not return an archive snapshot.");
    }
    catch (OperationCanceledException error)
    {
        Check(error.CancellationToken == cancellation.Token, "Refresh must preserve the caller's cancellation token.");
    }
    Check(File.Exists(path) && File.ReadAllBytes(path + ".note.json").SequenceEqual(note)
        && File.ReadAllBytes(path + ".keep").SequenceEqual(marker), "Canceling a read must leave videos and sidecars unchanged.");
    var restored = store.List(cancellationToken: CancellationToken.None).Single();
    Check(restored.Protected && restored.Label == "Behalten 🚗", "A later refresh must work normally after cancellation.");
}

static void CanceledListWaitingForStore()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    using var cleanupEntered = new ManualResetEventSlim();
    using var releaseCleanup = new ManualResetEventSlim();
    using var listCalled = new ManualResetEventSlim();
    using var cancellation = new CancellationTokenSource();
    var store = new RecordingStore(temp.Root, _ =>
    {
        cleanupEntered.Set();
        if (!releaseCleanup.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Isolated cleanup test was not released.");
        return 200L * 1024 * 1024 * 1024;
    });
    var cleanup = Task.Run(() => store.Cleanup(Settings(temp.Root), nowUtc: Now()));
    Task? pendingList = null;
    try
    {
        Check(cleanupEntered.Wait(TimeSpan.FromSeconds(5)), "The scratch store operation must hold its directory lock.");
        pendingList = Task.Run(() =>
        {
            listCalled.Set();
            try
            {
                store.List(cancellationToken: cancellation.Token);
                throw new InvalidOperationException("A canceled queued list must not complete successfully.");
            }
            catch (OperationCanceledException error)
            {
                Check(error.CancellationToken == cancellation.Token, "Queued refresh must preserve cancellation identity.");
            }
        });
        Check(listCalled.Wait(TimeSpan.FromSeconds(5)), "The queued refresh task must start.");
        cancellation.CancelAfter(100);
        Check(pendingList.Wait(TimeSpan.FromSeconds(2)), "Cancellation must finish while cleanup still holds the lock.");
        pendingList.GetAwaiter().GetResult();
        Check(!releaseCleanup.IsSet && !cleanup.IsCompleted, "List cancellation must not depend on finishing cleanup.");
    }
    finally
    {
        releaseCleanup.Set();
        cleanup.GetAwaiter().GetResult();
        // Always drain test work before disposing its events or deleting the scratch directory.
        try { pendingList?.GetAwaiter().GetResult(); } catch { }
    }
    Check(store.List().Single().Path == path && File.Exists(path), "Canceled lock acquisition must not strand the lock or alter the recording.");
}

static void CleanupSidecars()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddDays(-10), Now().AddDays(-9));
    var store = Store(temp.Root);
    store.SetLabel(path, "Abgelaufen");
    var unrelated = path + ".notes.txt";
    var orphan = Path.Combine(temp.Root, "holiday.mkv.note.json");
    File.WriteAllText(unrelated, "private note");
    File.WriteAllText(orphan, "unrelated metadata");
    store.SetProtected(path, true);
    Check(store.Cleanup(Settings(temp.Root), nowUtc: Now()).DeletedCount == 0, "Protected footage must remain protected automatically.");
    Check(File.Exists(path + ".keep") && File.Exists(path + ".note.json"), "Cleanup must keep sidecars while a protected video exists.");
    store.SetProtected(path, false);
    Check(store.Cleanup(Settings(temp.Root), nowUtc: Now()).DeletedCount == 1, "Expired unprotected footage should still be removed.");
    Check(!File.Exists(path + ".note.json") && File.Exists(unrelated) && File.Exists(orphan), "Cleanup may remove only the deleted recording's owned sidecars.");
}

static void ManualDeleteBoundary()
{
    using var temp = new Scratch();
    using var outside = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var external = Segment(outside.Root, Now().AddHours(-1), Now());
    var unrelated = Path.Combine(temp.Root, "holiday.mkv");
    File.WriteAllText(unrelated, "private");
    var store = Store(temp.Root);
    Throws<ArgumentException>(() => store.Delete(new[] { path, external }, false));
    Throws<ArgumentException>(() => store.Delete(new[] { path, unrelated }, false));
    var missing = Path.Combine(temp.Root, Prefix(Now(), "8765abcd") + "_000000.mkv");
    Throws<IOException>(() => store.Delete(new[] { path, missing }, false));
    Check(File.Exists(path) && File.Exists(external) && File.Exists(unrelated), "Any invalid or missing target must leave the entire batch untouched.");
    Check(store.Delete(Array.Empty<string>(), false) == 0, "An empty selection must be harmless.");
    Check(store.Delete(new[] { path, path }, false) == 1 && !File.Exists(path), "Duplicate paths should count and delete just once.");
}

static void ManualDeleteProtected()
{
    using var temp = new Scratch();
    var first = Segment(temp.Root, Now().AddHours(-2), Now().AddHours(-1));
    var protectedPath = Segment(temp.Root, Now().AddHours(-1), Now());
    var store = Store(temp.Root);
    store.SetProtected(protectedPath, true);
    store.SetLabel(protectedPath, "Wichtig");
    Throws<IOException>(() => store.Delete(new[] { first, protectedPath }, false));
    Check(File.Exists(first) && File.Exists(protectedPath) && File.Exists(protectedPath + ".keep"), "Protected refusal must preserve every selected file and its marker.");
    Check(store.Delete(new[] { first, protectedPath }, true) == 2, "Explicit confirmation should permit manual deletion of protected footage.");
    Check(!File.Exists(protectedPath + ".keep") && !File.Exists(protectedPath + ".note.json"), "Protected sidecars should be removed only after the confirmed video deletion.");
}

static void ManualDeleteActive()
{
    using var temp = new Scratch();
    var start = Now().AddHours(-1);
    var first = Segment(temp.Root, start, Now().AddMinutes(-5), 0);
    var active = Segment(temp.Root, start, Now(), 1);
    var store = Store(temp.Root);
    Throws<IOException>(() => store.Delete(new[] { first, active }, true, Prefix(start)));
    Check(File.Exists(first) && File.Exists(active), "The latest active segment must block the entire batch even with protected confirmation.");
    Check(store.Delete(new[] { first }, false, Prefix(start)) == 1 && File.Exists(active), "Closed segments from the active session remain eligible for manual deletion.");
}

static void ManualDeleteOpen()
{
    using var temp = new Scratch();
    var first = Segment(temp.Root, Now().AddHours(-2), Now().AddHours(-1));
    var opened = Segment(temp.Root, Now().AddHours(-1), Now());
    var store = Store(temp.Root);
    store.SetLabel(first, "Behalten");
    using (var open = new FileStream(opened, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
    {
        Throws<IOException>(() => store.Delete(new[] { first, opened }, false));
        Check(File.Exists(first) && File.Exists(opened) && File.Exists(first + ".note.json"), "An open last target must not cause partial deletion of earlier targets or metadata.");
    }
    Check(store.Delete(new[] { first, opened }, false) == 2, "The same batch should work after the media file is released.");
}

static void ManualDeleteReadOnly()
{
    using var temp = new Scratch();
    var first = Segment(temp.Root, Now().AddHours(-2), Now().AddHours(-1));
    var readOnly = Segment(temp.Root, Now().AddHours(-1), Now());
    File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
    try
    {
        Throws<IOException>(() => Store(temp.Root).Delete(new[] { first, readOnly }, false));
        Check(File.Exists(first) && File.Exists(readOnly), "A read-only last target must block the entire batch before any file is marked for deletion.");
    }
    finally { File.SetAttributes(readOnly, FileAttributes.Normal); }
}

static void ManualDeleteSidecars()
{
    using var temp = new Scratch();
    var path = Segment(temp.Root, Now().AddHours(-1), Now());
    var store = Store(temp.Root);
    store.SetLabel(path, "Notiz");
    var unrelated = path + ".other.json";
    var orphan = Path.Combine(temp.Root, Prefix(Now(), "8765abcd") + "_000000.mkv.note.json");
    File.WriteAllText(unrelated, "private");
    File.WriteAllText(orphan, "orphan left alone");
    Check(store.Delete(new[] { path }, false) == 1, "Selected video deletion should succeed.");
    Check(!File.Exists(path + ".note.json") && File.Exists(unrelated) && File.Exists(orphan), "Manual deletion should preserve unrelated suffixes and metadata for other paths.");
}

static void MetadataJunction()
{
    if (!OperatingSystem.IsWindows()) return;
    using var temp = new Scratch();
    using var outside = new Scratch();
    var path = Segment(temp.Root, Now().AddDays(-10), Now().AddDays(-9));
    var outsideNote = Path.Combine(outside.Root, "private.txt");
    File.WriteAllText(outsideNote, "must not change");
    var note = path + ".note.json";
    CreateJunction(note, outside.Root);
    try
    {
        var store = Store(temp.Root);
        Check(store.List().Single().Label == "", "A metadata junction must not be followed while listing.");
        Throws<IOException>(() => store.SetLabel(path, "unsafe"));
        Check(store.Cleanup(Settings(temp.Root), nowUtc: Now()).DeletedCount == 1, "An unsafe note should not prevent safe video retention cleanup.");
        Check(Directory.Exists(note) && File.ReadAllText(outsideNote) == "must not change", "Unsafe sidecar junctions and their external contents must be preserved.");
    }
    finally { Directory.Delete(note); }
    var second = Segment(temp.Root, Now().AddHours(-1), Now());
    var marker = second + ".keep";
    CreateJunction(marker, outside.Root);
    try
    {
        var store = Store(temp.Root);
        Throws<IOException>(() => store.Delete(new[] { second }, false));
        Check(store.Delete(new[] { second }, true) == 1, "Explicit protected confirmation may delete the owned video despite an unsafe marker.");
        Check(Directory.Exists(marker) && File.Exists(outsideNote), "Deleting a video must never delete or follow a marker junction.");
    }
    finally { Directory.Delete(marker); }
}

static void CreateJunction(string link, string target)
{
    using var process = Process.Start(new ProcessStartInfo("cmd.exe")
    {
        Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
    }) ?? throw new InvalidOperationException("Junction helper did not start.");
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException("Junction creation failed: " + process.StandardError.ReadToEnd());
}

internal sealed class Scratch : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ScreenDashTests-" + Guid.NewGuid().ToString("N"));
    public Scratch() => Directory.CreateDirectory(Root);
    public void Dispose() => Directory.Delete(Root, true);
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
