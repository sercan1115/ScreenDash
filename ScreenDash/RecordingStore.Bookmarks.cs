using System.Text.Json;

namespace ScreenDash;

public sealed partial class RecordingStore
{
    private const int MaximumBookmarks = 2000;
    private const int MaximumBookmarkBytes = 1024 * 1024;
    private sealed record BookmarkDocument(int Version, List<RecordingBookmark> Bookmarks);

    public IReadOnlyList<RecordingBookmark> GetBookmarks(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        while (!Monitor.TryEnter(_gate, 50)) ct.ThrowIfCancellationRequested();
        try
        {
            ct.ThrowIfCancellationRequested();
            EnsureSafeRoot(); path = ValidateOwnedPath(path); AssertBookmarkVideo(path);
            return ReadBookmarks(path, ct).OrderBy(b => b.Offset).ThenBy(b => b.CreatedUtc).ToArray();
        }
        finally { Monitor.Exit(_gate); }
    }
    public RecordingBookmark AddBookmark(string path, TimeSpan offset, string label, DateTime? createdUtc = null)
    {
        if (offset < TimeSpan.Zero || offset > TimeSpan.FromDays(365)) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (_gate)
        {
            EnsureSafeRoot(); path = ValidateOwnedPath(path); AssertBookmarkVideo(path);
            var bookmarks = ReadBookmarks(path, CancellationToken.None);
            if (bookmarks.Count >= MaximumBookmarks) throw new IOException("Die Aufnahme enthält bereits 2000 Lesezeichen.");
            var bookmark = new RecordingBookmark(Guid.NewGuid().ToString("N"), offset, (createdUtc ?? DateTime.UtcNow).ToUniversalTime(), ValidateLabel(label));
            bookmarks.Add(bookmark); WriteBookmarks(path, bookmarks); return bookmark;
        }
    }
    public void RenameBookmark(string path, string id, string label)
    {
        lock (_gate)
        {
            EnsureSafeRoot(); path = ValidateOwnedPath(path); AssertBookmarkVideo(path);
            var bookmarks = ReadBookmarks(path, CancellationToken.None);
            int index = bookmarks.FindIndex(b => b.Id == id);
            if (index < 0) throw new FileNotFoundException("Das Lesezeichen ist nicht mehr vorhanden.");
            bookmarks[index] = bookmarks[index] with { Label = ValidateLabel(label) }; WriteBookmarks(path, bookmarks);
        }
    }
    public void DeleteBookmark(string path, string id)
    {
        lock (_gate)
        {
            EnsureSafeRoot(); path = ValidateOwnedPath(path); AssertBookmarkVideo(path);
            var bookmarks = ReadBookmarks(path, CancellationToken.None);
            if (bookmarks.RemoveAll(b => b.Id == id) == 0) throw new FileNotFoundException("Das Lesezeichen ist nicht mehr vorhanden.");
            WriteBookmarks(path, bookmarks);
        }
    }
    private static void AssertBookmarkVideo(string path)
    {
        if (!TryGetAttributes(path, out var attributes)) throw new FileNotFoundException("Die Aufnahme ist nicht mehr vorhanden.", path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw new IOException("Lesezeichen erfordern eine normale Aufnahmedatei.");
    }
    private List<RecordingBookmark> ReadBookmarks(string path, CancellationToken ct)
    {
        string sidecar = path + ".bookmarks.json";
        if (!TryGetAttributes(sidecar, out var attributes)) return [];
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) throw new IOException("Die Lesezeichendatei ist eine Verknüpfung oder ein Ordner.");
        ct.ThrowIfCancellationRequested();
        try
        {
            using var handle = OpenSafeFile(sidecar, Native.GenericRead, Native.ShareRead);
            using var stream = new FileStream(handle, FileAccess.Read);
            if (stream.Length > MaximumBookmarkBytes) throw new IOException("Die Lesezeichendatei ist zu groß.");
            var document = JsonSerializer.Deserialize<BookmarkDocument>(stream, NoteOptions);
            ct.ThrowIfCancellationRequested();
            if (document is not { Version: 1 } || document.Bookmarks is null || document.Bookmarks.Count > MaximumBookmarks)
                throw new IOException("Die Lesezeichendatei hat ein ungültiges Format.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bookmark in document.Bookmarks)
            {
                ct.ThrowIfCancellationRequested();
                if (bookmark is null || !Guid.TryParseExact(bookmark.Id, "N", out var id) || id == Guid.Empty || bookmark.Id != id.ToString("N")
                    || !ids.Add(bookmark.Id) || bookmark.Offset < TimeSpan.Zero || bookmark.Offset > TimeSpan.FromDays(365) || bookmark.CreatedUtc.Kind != DateTimeKind.Utc)
                    throw new IOException("Die Lesezeichendatei enthält ungültige Einträge.");
                _ = ValidateLabel(bookmark.Label);
            }
            return document.Bookmarks;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { throw new IOException("Die Lesezeichendatei ist beschädigt und bleibt erhalten.", ex); }
    }
    private void WriteBookmarks(string path, List<RecordingBookmark> bookmarks)
    {
        string destination = path + ".bookmarks.json";
        string temporary = Path.Combine(_root, ".bookmark-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var handle = OpenSafeFile(temporary, Native.GenericWrite, 0, 1)) // CREATE_NEW: never follow or overwrite a preexisting temp path.
            using (var stream = new FileStream(handle, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, new BookmarkDocument(1, bookmarks), NoteOptions);
                if (stream.Length > MaximumBookmarkBytes) throw new IOException("Die Lesezeichen sind zu groß.");
                stream.Flush(true);
            }
            EnsureSafeRoot(); AssertBookmarkVideo(path);
            if (TryGetAttributes(destination, out var attributes) && (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Die Lesezeichendatei wurde durch eine Verknüpfung oder einen Ordner ersetzt.");
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) DeleteSafeSidecar(temporary); }
    }
}
