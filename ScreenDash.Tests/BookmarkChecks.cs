using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ScreenDash;

internal static class BookmarkChecks
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("bookmark roundtrip orders offsets and UTC creation times", Roundtrip),
        ("bookmark rename and delete preserve identities and recording protection", RenameDelete),
        ("bookmark validation preserves prior metadata on rejected input", Validation),
        ("bookmark metadata rejects corrupt entries without overwriting", CorruptPreserved),
        ("bookmark count and byte limits reject safely", Bounds),
        ("bookmarks refuse unrelated nested escaped and missing recording paths", OwnedPaths),
        ("bookmarks refuse directory recordings and directory sidecars", DirectoryBoundary),
        ("bookmark junctions remain untouched and never reach their target", JunctionBoundary),
        ("retention removes bookmark sidecars only after the owned video", RetentionSidecars),
        ("manual deletion preserves bookmark metadata until the entire batch validates", ManualSidecars),
        ("canceled bookmark reads preserve all recording metadata", CanceledRead),
        ("canceled bookmark reads do not wait for an occupied store lock", CanceledLock),
        ("locked bookmark metadata recovers after another program closes it", LockedSidecar)
    ];
    private static DateTime Now => new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static RecordingStore Store(string root) => new(root, _ => 200L * 1024 * 1024 * 1024);
    private static AppSettings Settings(string root) => new() { RecordingDirectory = root, RetentionDays = 7, MaxStorageGb = 50, MinFreeSpaceGb = 2 };
    private static string Video(string root, int index = 0, bool old = false)
    {
        var stamp = (old ? Now.AddDays(-10) : Now.AddHours(-1)).ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var path = Path.Combine(root, $"screendash_{stamp}_1234abcd_{index:D6}.mkv");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        File.SetLastWriteTimeUtc(path, old ? Now.AddDays(-9) : Now);
        return path;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void WriteDocument(string path, IReadOnlyList<RecordingBookmark> bookmarks, int version = 1)
        => File.WriteAllText(path + ".bookmarks.json", JsonSerializer.Serialize(new { Version = version, Bookmarks = bookmarks }));
    private static RecordingBookmark Bookmark(int index = 0) => new((index + 1).ToString("x32"), TimeSpan.FromSeconds(index), Now, "Moment " + index);

    private static void Roundtrip()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root);
        var later = store.AddBookmark(path, TimeSpan.FromSeconds(30), "  Später 🚗  ", Now);
        var first = store.AddBookmark(path, TimeSpan.Zero, "Beginn", Now);
        var sameOffsetNewer = store.AddBookmark(path, TimeSpan.FromSeconds(30), "Neuer", Now.AddSeconds(1));
        var loaded = Store(temp.Root).GetBookmarks(path);
        Require(loaded.SequenceEqual([first, later, sameOffsetNewer]) && loaded[1].Label == "Später 🚗", "Bookmarks must persist and sort by offset, then UTC creation time.");
        Require(loaded.All(b => b.CreatedUtc.Kind == DateTimeKind.Utc && Guid.TryParseExact(b.Id, "N", out _)), "Stored IDs and creation times must remain valid.");
        Require(!Directory.EnumerateFiles(temp.Root, ".bookmark-*").Any(), "Atomic writes must leave no temporary files.");
    }

    private static void RenameDelete()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root);
        store.SetProtected(path, true); var first = store.AddBookmark(path, TimeSpan.FromSeconds(10), "Moment", Now);
        var second = store.AddBookmark(path, TimeSpan.FromSeconds(20), "Zweiter", Now);
        store.RenameBookmark(path, first.Id, "  Treffer 🎯  ");
        var renamed = store.GetBookmarks(path).Single(b => b.Id == first.Id);
        Require(renamed.Id == first.Id && renamed.Offset == first.Offset && renamed.CreatedUtc == first.CreatedUtc && renamed.Label == "Treffer 🎯", "Renaming may only change the label.");
        store.DeleteBookmark(path, first.Id);
        Require(Store(temp.Root).GetBookmarks(path).SequenceEqual([second]) && File.Exists(path + ".keep"), "Deleting a bookmark must retain the other bookmark and file protection.");
        store.DeleteBookmark(path, second.Id);
        Require(store.GetBookmarks(path).Count == 0 && File.Exists(path) && File.Exists(path + ".keep"), "An empty bookmark list must not delete the recording or its protection.");
        Throws<FileNotFoundException>(() => store.DeleteBookmark(path, first.Id));
        Throws<FileNotFoundException>(() => store.RenameBookmark(path, first.Id, "Missing"));
    }

    private static void Validation()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root);
        var original = store.AddBookmark(path, TimeSpan.FromDays(365), string.Concat(Enumerable.Repeat("🚗", 120)), Now);
        var bytes = File.ReadAllBytes(path + ".bookmarks.json");
        Throws<ArgumentOutOfRangeException>(() => store.AddBookmark(path, TimeSpan.FromTicks(-1), "Invalid"));
        Throws<ArgumentOutOfRangeException>(() => store.AddBookmark(path, TimeSpan.FromDays(365).Add(TimeSpan.FromTicks(1)), "Invalid"));
        foreach (var label in new[] { new string('x', 121), "bad\nline", "bad\t", "\ud800" })
        {
            Throws<ArgumentException>(() => store.AddBookmark(path, TimeSpan.Zero, label));
            Throws<ArgumentException>(() => store.RenameBookmark(path, original.Id, label));
        }
        Require(File.ReadAllBytes(path + ".bookmarks.json").SequenceEqual(bytes), "Every rejected edit must leave the exact prior document intact.");
    }

    private static void CorruptPreserved()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root); var sidecar = path + ".bookmarks.json";
        var valid = Bookmark();
        var malformed = new List<string> { "{broken", "null", "{\"Version\":2,\"Bookmarks\":[]}", "{\"Version\":1,\"Bookmarks\":null}" };
        foreach (var entry in new[] { valid with { Id = "bad" }, valid with { Id = Guid.Empty.ToString("N") },
            valid with { Id = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }, valid with { Offset = TimeSpan.FromTicks(-1) },
            valid with { Offset = TimeSpan.FromDays(366) }, valid with { CreatedUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) },
            valid with { Label = "invalid\nlabel" }, valid with { Label = null! } })
            malformed.Add(JsonSerializer.Serialize(new { Version = 1, Bookmarks = new[] { entry } }));
        malformed.Add(JsonSerializer.Serialize(new { Version = 1, Bookmarks = new[] { valid, valid } }));
        foreach (var document in malformed)
        {
            File.WriteAllText(sidecar, document);
            Throws<IOException>(() => store.GetBookmarks(path));
            Throws<IOException>(() => store.AddBookmark(path, TimeSpan.Zero, "New"));
            Throws<IOException>(() => store.RenameBookmark(path, valid.Id, "New"));
            Throws<IOException>(() => store.DeleteBookmark(path, valid.Id));
            Require(File.ReadAllText(sidecar) == document && File.Exists(path), "Corrupt metadata and its recording must remain recoverable.");
        }
    }

    private static void Bounds()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root); var sidecar = path + ".bookmarks.json";
        WriteDocument(path, Enumerable.Range(0, 2000).Select(Bookmark).ToArray());
        Require(store.GetBookmarks(path).Count == 2000, "The documented maximum count must read successfully.");
        var original = File.ReadAllBytes(sidecar);
        Throws<IOException>(() => store.AddBookmark(path, TimeSpan.Zero, "Over limit"));
        Require(File.ReadAllBytes(sidecar).SequenceEqual(original), "A maximum-count document must not be rewritten.");
        WriteDocument(path, Enumerable.Range(0, 2001).Select(Bookmark).ToArray());
        Throws<IOException>(() => store.GetBookmarks(path));
        File.WriteAllBytes(sidecar, new byte[1024 * 1024 + 1]);
        Throws<IOException>(() => store.GetBookmarks(path));
        Throws<IOException>(() => store.AddBookmark(path, TimeSpan.Zero, "New"));
        Require(new FileInfo(sidecar).Length == 1024 * 1024 + 1, "An oversized document must remain unchanged.");
    }

    private static void OwnedPaths()
    {
        using var temp = new Scratch(); using var outside = new Scratch();
        var path = Video(temp.Root); var external = Video(outside.Root);
        var nested = Directory.CreateDirectory(Path.Combine(temp.Root, "nested")).FullName;
        var nestedVideo = Video(nested); var unrelated = Path.Combine(temp.Root, "holiday.mkv"); File.WriteAllText(unrelated, "private");
        var store = Store(temp.Root);
        foreach (var invalid in new[] { external, nestedVideo, unrelated, Path.Combine(temp.Root, "..", Path.GetFileName(path)) })
        {
            Throws<ArgumentException>(() => store.GetBookmarks(invalid));
            Throws<ArgumentException>(() => store.AddBookmark(invalid, TimeSpan.Zero, "Invalid"));
            Throws<ArgumentException>(() => store.RenameBookmark(invalid, Bookmark().Id, "Invalid"));
            Throws<ArgumentException>(() => store.DeleteBookmark(invalid, Bookmark().Id));
        }
        var missing = Path.Combine(temp.Root, Path.GetFileName(path).Replace("1234abcd", "8765abcd"));
        Throws<FileNotFoundException>(() => store.GetBookmarks(missing));
        Throws<FileNotFoundException>(() => store.AddBookmark(missing, TimeSpan.Zero, "Missing"));
        Require(!File.Exists(external + ".bookmarks.json") && !File.Exists(unrelated + ".bookmarks.json"), "No invalid target may acquire bookmark metadata.");
    }

    private static void DirectoryBoundary()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root);
        File.Delete(path); Directory.CreateDirectory(path);
        Throws<IOException>(() => store.GetBookmarks(path));
        Throws<IOException>(() => store.AddBookmark(path, TimeSpan.Zero, "Directory"));
        Throws<IOException>(() => store.RenameBookmark(path, Bookmark().Id, "Directory"));
        Throws<IOException>(() => store.DeleteBookmark(path, Bookmark().Id));
        Directory.Delete(path); path = Video(temp.Root); var sidecar = path + ".bookmarks.json"; Directory.CreateDirectory(sidecar);
        Throws<IOException>(() => store.GetBookmarks(path));
        Throws<IOException>(() => store.AddBookmark(path, TimeSpan.Zero, "Sidecar directory"));
        Require(Directory.Exists(sidecar) && File.Exists(path), "Unsafe directory metadata must remain untouched.");
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Junction helper did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("Isolated junction creation failed: " + process.StandardError.ReadToEnd());
    }

    private static void JunctionBoundary()
    {
        using var temp = new Scratch(); using var outside = new Scratch();
        var privateFile = Path.Combine(outside.Root, "private.txt"); File.WriteAllText(privateFile, "must remain");
        var path = Video(temp.Root, old: true); var sidecar = path + ".bookmarks.json"; CreateJunction(sidecar, outside.Root);
        try
        {
            var store = Store(temp.Root);
            Throws<IOException>(() => store.GetBookmarks(path));
            Throws<IOException>(() => store.AddBookmark(path, TimeSpan.Zero, "Unsafe"));
            Throws<IOException>(() => store.RenameBookmark(path, Bookmark().Id, "Unsafe"));
            Throws<IOException>(() => store.DeleteBookmark(path, Bookmark().Id));
            Require(store.Cleanup(Settings(temp.Root), nowUtc: Now).DeletedCount == 1, "Unsafe sidecar must not block safe removal of its expired video.");
            Require(Directory.Exists(sidecar) && File.ReadAllText(privateFile) == "must remain", "Cleanup must preserve the junction and its external target.");
        }
        finally { Directory.Delete(sidecar); }
        path = Video(temp.Root); File.Delete(path); CreateJunction(path, outside.Root);
        try
        {
            var store = Store(temp.Root);
            Throws<IOException>(() => store.GetBookmarks(path));
            Throws<IOException>(() => store.AddBookmark(path, TimeSpan.Zero, "Unsafe video"));
            Require(File.ReadAllText(privateFile) == "must remain", "A video junction must not lead to external mutations.");
        }
        finally { Directory.Delete(path); }
    }

    private static void RetentionSidecars()
    {
        using var temp = new Scratch(); var path = Video(temp.Root, old: true); var store = Store(temp.Root);
        store.AddBookmark(path, TimeSpan.Zero, "Expired"); store.SetProtected(path, true);
        var sidecar = path + ".bookmarks.json"; var unrelated = path + ".other-bookmarks.json"; File.WriteAllText(unrelated, "private");
        Require(store.Cleanup(Settings(temp.Root), nowUtc: Now).DeletedCount == 0 && File.Exists(sidecar), "Protected video must retain bookmark metadata.");
        store.SetProtected(path, false);
        Require(store.Cleanup(Settings(temp.Root), nowUtc: Now).DeletedCount == 1 && !File.Exists(sidecar) && File.Exists(unrelated), "Retention may delete only its video's own sidecar.");
    }

    private static void ManualSidecars()
    {
        using var temp = new Scratch(); var first = Video(temp.Root); var second = Video(temp.Root, 1); var store = Store(temp.Root);
        store.AddBookmark(first, TimeSpan.Zero, "First"); store.AddBookmark(second, TimeSpan.Zero, "Second"); store.SetProtected(second, true);
        Throws<IOException>(() => store.Delete([first, second], false));
        Require(File.Exists(first) && File.Exists(second) && File.Exists(first + ".bookmarks.json") && File.Exists(second + ".bookmarks.json"), "Rejected batch must preserve all videos and bookmarks.");
        Require(store.Delete([first, second], true) == 2 && !File.Exists(first + ".bookmarks.json") && !File.Exists(second + ".bookmarks.json"), "Validated manual deletion must remove owned bookmark sidecars.");
    }

    private static void CanceledRead()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root);
        var bookmark = store.AddBookmark(path, TimeSpan.Zero, "Keep"); var before = File.ReadAllBytes(path + ".bookmarks.json");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { store.GetBookmarks(path, canceled.Token); throw new InvalidOperationException("A canceled read must throw."); }
        catch (OperationCanceledException error) { Require(error.CancellationToken == canceled.Token, "Cancellation must preserve token identity."); }
        Require(File.Exists(path) && File.ReadAllBytes(path + ".bookmarks.json").SequenceEqual(before) && store.GetBookmarks(path).Single() == bookmark, "Cancellation must preserve data and permit a later read.");
    }

    private static void CanceledLock()
    {
        using var temp = new Scratch(); var path = Video(temp.Root);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var called = new ManualResetEventSlim(); using var cancellation = new CancellationTokenSource();
        var store = new RecordingStore(temp.Root, _ => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Cleanup lock test timed out."); return 200L * 1024 * 1024 * 1024; });
        store.AddBookmark(path, TimeSpan.Zero, "Keep");
        var cleanup = Task.Run(() => store.Cleanup(Settings(temp.Root), nowUtc: Now)); Task? read = null;
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "Cleanup must hold the isolated store lock.");
            read = Task.Run(() => { called.Set(); Throws<OperationCanceledException>(() => store.GetBookmarks(path, cancellation.Token)); });
            Require(called.Wait(TimeSpan.FromSeconds(5)), "The pending bookmark read must run."); cancellation.CancelAfter(100);
            Require(read.Wait(TimeSpan.FromSeconds(2)) && !cleanup.IsCompleted, "Bookmark cancellation must complete without waiting for cleanup."); read.GetAwaiter().GetResult();
        }
        finally { release.Set(); cleanup.GetAwaiter().GetResult(); try { read?.GetAwaiter().GetResult(); } catch { } }
        Require(store.GetBookmarks(path).Count == 1, "Canceling lock acquisition must not strand its lock.");
    }

    private static void LockedSidecar()
    {
        using var temp = new Scratch(); var path = Video(temp.Root); var store = Store(temp.Root); var original = store.AddBookmark(path, TimeSpan.Zero, "Original");
        var before = File.ReadAllBytes(path + ".bookmarks.json");
        using (var locked = new FileStream(path + ".bookmarks.json", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Throws<IOException>(() => store.GetBookmarks(path));
            Throws<IOException>(() => store.AddBookmark(path, TimeSpan.FromSeconds(1), "Blocked"));
        }
        Require(File.ReadAllBytes(path + ".bookmarks.json").SequenceEqual(before) && store.GetBookmarks(path).Single() == original, "Transient locks must not corrupt metadata or poison later reads.");
    }
}
