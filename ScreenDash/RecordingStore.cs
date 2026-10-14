using System.Collections.Concurrent;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace ScreenDash;

/// <summary>Owns only ScreenDash segment files directly inside one local, non-reparse directory.</summary>
public sealed partial class RecordingStore
{
    private static readonly Regex SegmentName = new(
        @"\Ascreendash_(?<stamp>[0-9]{8}T[0-9]{9}Z)_(?<id>[0-9a-fA-F]{8})_(?<sequence>[0-9]{6})\.mkv\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, object> DirectoryLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root;
    private readonly object _gate;
    private readonly Func<string, long> _availableFreeBytes;
    private const int MaximumNoteBytes = 4096;
    private static readonly JsonSerializerOptions NoteOptions = new() { MaxDepth = 8 };
    private readonly Dictionary<string, NoteFingerprint> _invalidNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedNote> _notes = new(StringComparer.OrdinalIgnoreCase);

    public RecordingStore(string root) : this(root, GetAvailableFreeBytes) { }

    // The injection lets reserve/quota behavior be verified without filling a user's drive.
    public RecordingStore(string root, Func<string, long> availableFreeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("Der Aufnahmeordner muss ein vollständiger Pfad sein.", nameof(root));
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _availableFreeBytes = availableFreeBytes ?? throw new ArgumentNullException(nameof(availableFreeBytes));
        _gate = DirectoryLocks.GetOrAdd(_root, static _ => new object());
        lock (_gate)
        {
            EnsureSafeRoot();
            Directory.CreateDirectory(_root);
            EnsureSafeRoot();
        }
    }

    public IReadOnlyList<RecordingInfo> List(string? activeSessionPrefix = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A canceled refresh need not wait for a concurrent cleanup or another list scan.
        // Mutation methods still use the same directory lock and their fresh safety checks.
        if (cancellationToken.CanBeCanceled)
        {
            while (!Monitor.TryEnter(_gate, 50)) cancellationToken.ThrowIfCancellationRequested();
        }
        else Monitor.Enter(_gate);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeRoot();
            var scan = Scan(activeSessionPrefix, loadLabels: true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var recordings = scan.Entries.OrderByDescending(e => e.CompletedUtc).ThenByDescending(e => e.Path, StringComparer.Ordinal)
                .Select(e =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new RecordingInfo(e.Path, e.StartedUtc, e.Bytes, e.Protected, e.Active, e.Label);
                }).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return recordings;
        }
        finally { Monitor.Exit(_gate); }
    }

    public CleanupResult Cleanup(AppSettings settings, string? activeSessionPrefix = null, DateTime? nowUtc = null)
    {
        var validated = SettingsStore.Validate(settings);
        lock (_gate)
        {
            long total = 0, protectedBytes = 0, deletedBytes = 0, free = 0;
            var deletedCount = 0;
            try
            {
                EnsureSafeRoot();
                var scan = Scan(activeSessionPrefix);
                total = scan.Entries.Sum(e => e.Bytes);
                protectedBytes = scan.Entries.Where(e => e.Protected).Sum(e => e.Bytes);
                free = Math.Max(0, _availableFreeBytes(_root));
                var now = NormalizeUtc(nowUtc ?? DateTime.UtcNow);
                var cutoff = now.AddDays(-validated.RetentionDays);
                var quota = validated.MaxStorageGb * 1024L * 1024 * 1024;
                var reserve = validated.MinFreeSpaceGb * 1024L * 1024 * 1024;
                // Cover the controller's 10-second check interval plus bitrate/muxing variance.
                // This is an estimate: encoder spikes still need the periodic stop check.
                var headroom = Math.Max(8L * 1024 * 1024,
                    (long)Math.Ceiling((validated.VideoBitrateKbps + (validated.AudioEnabled || validated.MicrophoneEnabled ? 128 : 0)) * 1000d / 8 * 15 * 1.25));

                foreach (var entry in scan.Entries.OrderBy(e => e.CompletedUtc).ThenBy(e => e.Path, StringComparer.Ordinal))
                {
                    var expired = entry.CompletedUtc < cutoff;
                    var overQuota = total > quota - headroom;
                    var lowSpace = free < reserve + headroom;
                    if (!expired && !overQuota && !lowSpace) continue;
                    if (entry.Protected || entry.Active || !TryDelete(entry)) continue;
                    deletedCount++;
                    deletedBytes += entry.Bytes;
                    total -= entry.Bytes;
                    free = Math.Max(0, _availableFreeBytes(_root));
                }

                // A recording can grow while cleanup runs. Re-read lengths and markers before deciding.
                var finalScan = Scan(activeSessionPrefix);
                total = finalScan.Entries.Sum(e => e.Bytes);
                protectedBytes = finalScan.Entries.Where(e => e.Protected).Sum(e => e.Bytes);
                free = Math.Max(0, _availableFreeBytes(_root));
                var canRecord = !scan.HadErrors && !finalScan.HadErrors && total <= quota - headroom && free >= reserve + headroom;
                var message = canRecord
                    ? (deletedCount == 0 ? "Speicher bereit." : $"{deletedCount} alte Aufnahme(n) gelöscht.")
                    : scan.HadErrors || finalScan.HadErrors
                        ? "Aufnahmedateien konnten nicht sicher geprüft werden. Bitte Ordnerzugriff prüfen."
                        : total > quota - headroom
                            ? "Speicherlimit erreicht. Geschützte oder verwendete Aufnahmen freigeben oder das Limit erhöhen."
                            : "Zu wenig freier Speicher. Bitte Speicher freigeben oder einen anderen Aufnahmeordner wählen.";
                return new(deletedCount, deletedBytes, total, protectedBytes, free, canRecord, message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AppLog.Write(ex);
                return new(deletedCount, deletedBytes, total, protectedBytes, free, false,
                    "Speicherprüfung fehlgeschlagen. Bitte einen zugänglichen lokalen Aufnahmeordner wählen: " + ex.Message);
            }
        }
    }

    public int ProtectRecent(int minutes, string? activeSessionPrefix = null, DateTime? nowUtc = null)
    {
        if (minutes < 1 || minutes > 1440)
            throw new ArgumentOutOfRangeException(nameof(minutes), "Der Schutzzeitraum muss zwischen 1 und 1440 Minuten liegen.");
        lock (_gate)
        {
            EnsureSafeRoot();
            var now = NormalizeUtc(nowUtc ?? DateTime.UtcNow);
            var cutoff = now.AddMinutes(-minutes);
            var count = 0;
            foreach (var entry in Scan(activeSessionPrefix).Entries)
            {
                // Completion time catches every segment overlapping the requested window.
                // The active file may be empty and has not necessarily updated its timestamp yet.
                if (!entry.Active && !(entry.StartedUtc <= now && entry.CompletedUtc >= cutoff)) continue;
                if (entry.Protected) continue;
                SetProtectedCore(entry.Path, true);
                count++;
            }
            return count;
        }
    }

    public void SetProtected(string path, bool protect)
    {
        lock (_gate)
        {
            EnsureSafeRoot();
            SetProtectedCore(ValidateOwnedPath(path), protect);
        }
    }

    public void SetLabel(string path, string label)
    {
        var normalized = ValidateLabel(label);
        lock (_gate)
        {
            EnsureSafeRoot();
            path = ValidateOwnedPath(path);
            var video = new FileInfo(path);
            video.Refresh();
            if (!video.Exists) throw new FileNotFoundException("Die Aufnahme ist nicht mehr vorhanden.", path);
            if ((video.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Verknüpfungen und Ordner können nicht beschriftet werden.");
            var note = path + ".note.json";
            if (TryGetAttributes(note, out var attributes) && (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Die Beschriftungsdatei ist eine Verknüpfung oder ein Ordner. Bitte manuell prüfen.");
            if (normalized.Length == 0)
            {
                if (File.Exists(note)) DeleteSafeSidecar(note);
                _invalidNotes.Remove(note);
                _notes.Remove(note);
                return;
            }
            var temporary = Path.Combine(_root, ".screendash-note-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, new LabelNote(normalized), NoteOptions);
                    stream.Flush(true);
                }
                EnsureSafeRoot();
                if (TryGetAttributes(note, out attributes) && (attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new IOException("Die Beschriftungsdatei wurde durch eine Verknüpfung oder einen Ordner ersetzt.");
                File.Move(temporary, note, true);
                _invalidNotes.Remove(note);
                _notes.Remove(note);
            }
            finally
            {
                try { if (File.Exists(temporary)) DeleteSafeSidecar(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
            }
        }
    }

    /// <summary>Validates and exclusively reserves the complete batch before deleting any video.</summary>
    public int Delete(IReadOnlyList<string> paths, bool allowProtected, string? activeSessionPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            EnsureSafeRoot();
            var targets = paths.Select(ValidateOwnedPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (targets.Length == 0) return 0;
            var scan = Scan(activeSessionPrefix);
            if (scan.HadErrors) throw new IOException("Die Aufnahmen konnten nicht sicher geprüft werden. Es wurde nichts gelöscht.");
            var entries = scan.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
            var pending = new List<(Entry Entry, SafeFileHandle Handle)>();
            var marked = new List<SafeFileHandle>();
            try
            {
                foreach (var target in targets)
                {
                    if (!entries.TryGetValue(target, out var entry))
                        throw new IOException("Eine ausgewählte Aufnahme ist nicht mehr vorhanden oder keine sichere Aufnahmedatei. Es wurde nichts gelöscht.");
                    AssertDeletable(entry, allowProtected);
                    var handle = OpenSafeFile(target, Native.GenericRead | Native.DeleteAccess, 0);
                    pending.Add((entry, handle));
                    var actual = GetHandleInformation(handle);
                    if (!Matches(entry, actual))
                        throw new IOException("Eine ausgewählte Aufnahme wurde geändert. Bitte die Liste aktualisieren. Es wurde nichts gelöscht.");
                    if ((actual.Attributes & FileAttributes.ReadOnly) != 0)
                        throw new IOException("Eine ausgewählte Aufnahme ist schreibgeschützt. Es wurde nichts gelöscht.");
                }
                // Refresh active/protection state after all handles are held; nothing has been deleted yet.
                EnsureSafeRoot();
                var refreshed = Scan(activeSessionPrefix);
                if (refreshed.HadErrors) throw new IOException("Die Aufnahmen konnten nicht sicher geprüft werden. Es wurde nichts gelöscht.");
                var refreshedEntries = refreshed.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
                foreach (var item in pending)
                {
                    if (!refreshedEntries.TryGetValue(item.Entry.Path, out var current))
                        throw new IOException("Eine ausgewählte Aufnahme wurde geändert. Es wurde nichts gelöscht.");
                    AssertDeletable(current, allowProtected);
                    if (!Matches(current, GetHandleInformation(item.Handle)))
                        throw new IOException("Eine ausgewählte Aufnahme wurde geändert. Es wurde nichts gelöscht.");
                }
                // A reserved DELETE handle gives DeleteOnClose semantics without marking any file
                // during validation. Share.None holds every target until the whole batch is ready.
                foreach (var item in pending)
                {
                    SetDeleteOnClose(item.Handle, true);
                    marked.Add(item.Handle);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                foreach (var handle in marked)
                {
                    try { SetDeleteOnClose(handle, false); }
                    catch (IOException rollbackError) { AppLog.Write(rollbackError); }
                }
                throw new IOException("Die ausgewählten Aufnahmen konnten nicht gelöscht werden: " + ex.Message, ex);
            }
            finally
            {
                foreach (var item in pending) item.Handle.Dispose();
            }
            var deleted = 0;
            foreach (var item in pending)
            {
                if (File.Exists(item.Entry.Path)) continue;
                deleted++;
                RemoveOwnedSidecars(item.Entry.Path);
            }
            return deleted;
        }
    }

    private static void AssertDeletable(Entry entry, bool allowProtected)
    {
        if (entry.Active) throw new IOException("Die laufende Aufnahme kann nicht gelöscht werden. Es wurde nichts gelöscht.");
        if (entry.Protected && !allowProtected)
            throw new IOException("Eine ausgewählte Aufnahme ist geschützt. Bitte das Löschen geschützter Aufnahmen ausdrücklich bestätigen.");
    }

    private static string ValidateLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (label.Any(char.IsControl)) throw new ArgumentException("Die Beschriftung darf keine Steuerzeichen oder Zeilenumbrüche enthalten.", nameof(label));
        try { new UTF8Encoding(false, true).GetByteCount(label); }
        catch (EncoderFallbackException ex) { throw new ArgumentException("Die Beschriftung enthält ungültige Unicode-Zeichen.", nameof(label), ex); }
        var normalized = label.Trim();
        if (normalized.EnumerateRunes().Count() > 120)
            throw new ArgumentException("Die Beschriftung darf höchstens 120 Zeichen enthalten.", nameof(label));
        return normalized;
    }

    private void SetProtectedCore(string path, bool protect)
    {
        path = ValidateOwnedPath(path);
        if (!TryGetAttributes(path, out var fileAttributes))
            throw new FileNotFoundException("Die Aufnahme ist nicht mehr vorhanden.", path);
        if ((fileAttributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Verknüpfungen und Ordner können nicht als Aufnahme geschützt werden.");
        var marker = path + ".keep";
        if (TryGetAttributes(marker, out var markerAttributes) &&
            (markerAttributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Die Schutzmarkierung ist eine Verknüpfung oder ein Ordner. Bitte manuell prüfen.");
        if (protect)
        {
            // Creating the sidecar also works while the recorder has the MKV open.
            using var handle = OpenSafeFile(marker, Native.GenericWrite, Native.ShareRead, Native.OpenAlways);
            using var stream = new FileStream(handle, FileAccess.Write);
            if (stream.Length == 0)
            {
                var data = Encoding.UTF8.GetBytes("ScreenDash: vor automatischem Löschen geschützt.\n" + DateTime.UtcNow.ToString("O") + "\n");
                stream.Write(data);
                stream.Flush(true);
            }
        }
        else if (File.Exists(marker)) DeleteSafeSidecar(marker);
    }

    private bool TryDelete(Entry entry)
    {
        try
        {
            EnsureSafeRoot();
            if (IsProtected(entry.Path)) return false;
            var info = new FileInfo(entry.Path);
            info.Refresh();
            if (!info.Exists || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return false;
            if (info.Length != entry.Bytes || info.LastWriteTimeUtc != entry.LastWriteUtc || info.CreationTimeUtc != entry.CreatedUtc) return false;
            // Exclusive, non-following DELETE handles skip files in use. Marking the exact
            // validated handle for DeleteOnClose avoids a close/delete or symlink race.
            using (var handle = OpenSafeFile(entry.Path, Native.GenericRead | Native.DeleteAccess, 0))
            {
                if (!Matches(entry, GetHandleInformation(handle)) || IsProtected(entry.Path)) return false;
                SetDeleteOnClose(handle, true);
            }
            if (File.Exists(entry.Path)) return false;
            RemoveOwnedSidecars(entry.Path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("Aufnahme bleibt erhalten (verwendet oder nicht löschbar): " + Path.GetFileName(entry.Path));
            return false;
        }
    }

    private ScanResult Scan(string? activePrefix, bool loadLabels = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = new List<Entry>();
        var hadErrors = false;
        var prefix = NormalizeActivePrefix(activePrefix);
        var videos = new List<(FileInfo Info, DateTime Started, string SessionPrefix)>();
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
        // Windows supplies attributes, length and timestamps with the directory enumeration.
        // Keep that snapshot instead of issuing Refresh plus sidecar stat calls for every video.
        // Destructive operations still revalidate each exact file and its protection marker.
        foreach (var info in new DirectoryInfo(_root).EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false
        }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (info.Name.EndsWith(".keep", StringComparison.OrdinalIgnoreCase)) markers.Add(info.FullName);
            if (loadLabels && info.Name.EndsWith(".note.json", StringComparison.OrdinalIgnoreCase)) notes[info.FullName] = info;
            if (info is FileInfo file && TryParseName(info.Name, out var started, out var sessionPrefix))
                videos.Add((file, started, sessionPrefix));
        }
        if (loadLabels)
        {
            // Entries for removed videos/notes must not accumulate across days of loop recording.
            var videoNotes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var video in videos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                videoNotes.Add(video.Info.FullName + ".note.json");
            }
            foreach (var key in _notes.Keys.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!notes.ContainsKey(key) || !videoNotes.Contains(key)) _notes.Remove(key);
            }
            foreach (var key in _invalidNotes.Keys.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!notes.ContainsKey(key) || !videoNotes.Contains(key)) _invalidNotes.Remove(key);
            }
        }
        foreach (var (info, started, sessionPrefix) in videos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!info.Exists || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) continue;
                var path = info.FullName;
                var lastWrite = info.LastWriteTimeUtc;
                var completion = lastWrite.Year >= 1980 ? lastWrite : started;
                // Each FFmpeg segment is created when its interval starts. Bound the birth
                // timestamp because copied files may have a newer creation date.
                var created = info.CreationTimeUtc;
                var segmentStart = created >= started && created <= completion ? created : started;
                entries.Add(new Entry(path, segmentStart, completion, lastWrite, created, info.Length, markers.Contains(path + ".keep"), false, sessionPrefix,
                    loadLabels ? ReadLabel(path, notes.GetValueOrDefault(path + ".note.json"), cancellationToken) : ""));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                hadErrors = true;
                AppLog.Write(ex);
            }
        }
        if (prefix is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = entries.Where(e => StringComparer.Ordinal.Equals(e.SessionPrefix, prefix))
                .OrderByDescending(e => e.Path, StringComparer.Ordinal).FirstOrDefault();
            if (latest is not null)
            {
                var index = entries.IndexOf(latest);
                entries[index] = latest with { Active = true };
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(entries, hadErrors);
    }

    private string ReadLabel(string videoPath, FileSystemInfo? noteInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var note = videoPath + ".note.json";
        var fingerprint = new NoteFingerprint(-1, DateTime.MinValue, DateTime.MinValue, default);
        try
        {
            if (noteInfo is null) { _invalidNotes.Remove(note); _notes.Remove(note); return ""; }
            var attributes = noteInfo.Attributes;
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Die Beschriftungsdatei ist eine Verknüpfung oder ein Ordner.");
            var info = (FileInfo)noteInfo;
            fingerprint = new(info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc, attributes);
            // Only display metadata is cached. Protection and every deletion remain independent.
            // Changes by another store/program invalidate the cached label through this snapshot.
            if (_notes.TryGetValue(note, out var cached) && cached.Fingerprint == fingerprint) return cached.Label;
            if (info.Length > MaximumNoteBytes) throw new JsonException("Die Beschriftungsdatei ist zu groß.");
            cancellationToken.ThrowIfCancellationRequested();
            using var handle = OpenSafeFile(note, Native.GenericRead, Native.ShareRead);
            using var stream = new FileStream(handle, FileAccess.Read);
            var bytes = new byte[MaximumNoteBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(bytes, length, bytes.Length - length);
                if (read == 0) break;
                length += read;
            }
            if (length > MaximumNoteBytes) throw new JsonException("Die Beschriftungsdatei ist zu groß.");
            var metadata = JsonSerializer.Deserialize<LabelNote>(bytes.AsSpan(0, length), NoteOptions)
                ?? throw new JsonException("Die Beschriftungsdatei ist leer.");
            var label = ValidateLabel(metadata.Label);
            cancellationToken.ThrowIfCancellationRequested();
            _invalidNotes.Remove(note);
            _notes[note] = new(fingerprint, label);
            return label;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_invalidNotes.TryGetValue(note, out var previous) || previous != fingerprint)
            {
                AppLog.Write("Beschriftung ignoriert: " + Path.GetFileName(note) + " – " + ex.Message);
                _invalidNotes[note] = fingerprint;
            }
            // I/O failures can recover without a changed timestamp (for example an editor lock).
            // Cache only invalid contents, not failures to access the file.
            if (ex is JsonException or ArgumentException) _notes[note] = new(fingerprint, "");
            else _notes.Remove(note);
            return "";
        }
    }

    private void RemoveOwnedSidecars(string videoPath)
    {
        // Never remove a marker while its video still exists. Only these exact suffixes belong to us.
        if (File.Exists(videoPath)) return;
        foreach (var suffix in new[] { ".keep", ".note.json", ".bookmarks.json" })
        {
            try
            {
                EnsureSafeRoot();
                var sidecar = videoPath + suffix;
                if (!TryGetAttributes(sidecar, out var attributes)) continue;
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                {
                    AppLog.Write("Unsichere Begleitdatei bleibt erhalten: " + Path.GetFileName(sidecar));
                    continue;
                }
                DeleteSafeSidecar(sidecar);
                _invalidNotes.Remove(sidecar);
                _notes.Remove(sidecar);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
        }
    }

    private static void DeleteSafeSidecar(string path)
    {
        using var handle = OpenSafeFile(path, Native.GenericRead | Native.DeleteAccess, 0);
        SetDeleteOnClose(handle, true);
    }

    private static SafeFileHandle OpenSafeFile(string path, uint access, uint sharing, uint creation = Native.OpenExisting)
    {
        var handle = Native.CreateFile(path, access, sharing, IntPtr.Zero, creation, Native.OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException("Die Datei wird verwendet oder ist nicht zugänglich: " + Path.GetFileName(path) + ". " + new Win32Exception(error).Message);
        }
        try
        {
            var info = GetHandleInformation(handle);
            if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Verknüpfungen und Ordner werden nicht geöffnet: " + Path.GetFileName(path));
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static Native.FileInformation GetHandleInformation(SafeFileHandle handle)
    {
        if (!Native.GetFileInformationByHandle(handle, out var info))
            throw new IOException("Die Datei konnte nicht sicher geprüft werden: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        return info;
    }

    private static bool Matches(Entry expected, Native.FileInformation actual) =>
        (actual.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0 &&
        ((long)actual.FileSizeHigh << 32 | actual.FileSizeLow) == expected.Bytes &&
        actual.LastWriteUtc == expected.LastWriteUtc && actual.CreatedUtc == expected.CreatedUtc;

    private static void SetDeleteOnClose(SafeFileHandle handle, bool delete)
    {
        var disposition = new Native.FileDisposition { DeleteFile = delete ? 1 : 0 };
        if (!Native.SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf<Native.FileDisposition>()))
            throw new IOException("Die Datei konnte nicht zum Löschen freigegeben werden: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
    }

    private string ValidateOwnedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(full), _root) ||
            !TryParseName(Path.GetFileName(full), out _, out _))
            throw new ArgumentException("Nur ScreenDash-Aufnahmen direkt im Aufnahmeordner dürfen geändert werden.", nameof(path));
        return full;
    }

    private static bool TryParseName(string name, out DateTime startedUtc, out string prefix)
    {
        startedUtc = default;
        prefix = "";
        var match = SegmentName.Match(name);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["stamp"].Value, "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out startedUtc)) return false;
        prefix = name[..(name.Length - "_000000.mkv".Length)];
        return true;
    }

    private static string? NormalizeActivePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return null;
        var stem = Path.GetFileName(prefix.TrimEnd('_'));
        return TryParseName(stem + "_000000.mkv", out _, out var parsed) ? parsed : null;
    }

    private static bool IsProtected(string path)
    {
        try { return TryGetAttributes(path + ".keep", out _); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try { attributes = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { attributes = default; return false; }
        catch (DirectoryNotFoundException) { attributes = default; return false; }
    }

    private void EnsureSafeRoot()
    {
        for (string? path = _root; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
        {
            if (TryGetAttributes(path, out var attributes) && (attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der Aufnahmeordner darf keine Verknüpfung oder Junction enthalten. Bitte einen direkten lokalen Ordner wählen.");
        }
    }

    private static long GetAvailableFreeBytes(string root)
    {
        var volume = Path.GetPathRoot(root);
        if (string.IsNullOrEmpty(volume) || volume.StartsWith(@"\\", StringComparison.Ordinal))
            throw new IOException("Bitte einen lokalen Aufnahmeordner wählen; Netzlaufwerke unterstützen die Speicherprüfung nicht.");
        var drive = new DriveInfo(volume);
        if (drive.DriveType == DriveType.Network)
            throw new IOException("Bitte einen lokalen Aufnahmeordner wählen; Netzlaufwerke unterstützen die Speicherprüfung nicht.");
        return drive.AvailableFreeSpace;
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed record Entry(string Path, DateTime StartedUtc, DateTime CompletedUtc, DateTime LastWriteUtc, DateTime CreatedUtc,
        long Bytes, bool Protected, bool Active, string SessionPrefix, string Label);
    private sealed record LabelNote(string Label);
    private readonly record struct NoteFingerprint(long Bytes, DateTime ChangedUtc, DateTime CreatedUtc, FileAttributes Attributes);
    private sealed record CachedNote(NoteFingerprint Fingerprint, string Label);
    private sealed record ScanResult(List<Entry> Entries, bool HadErrors);

    private static class Native
    {
        internal const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, DeleteAccess = 0x00010000,
            ShareRead = 1, OpenExisting = 3, OpenAlways = 4, OpenReparsePoint = 0x00200000;
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileTime
        {
            internal uint Low, High;
            internal readonly DateTime Utc => DateTime.FromFileTimeUtc(((long)High << 32) | Low);
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileInformation
        {
            internal FileAttributes Attributes;
            internal FileTime CreationTime, LastAccessTime, LastWriteTime;
            internal uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
            internal readonly DateTime CreatedUtc => CreationTime.Utc;
            internal readonly DateTime LastWriteUtc => LastWriteTime.Utc;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileDisposition { internal int DeleteFile; }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint access, uint sharing, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass,
            ref FileDisposition information, uint size);
    }
}
