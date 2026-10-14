namespace ScreenDash;

public sealed class AppSettings
{
    public string RecordingDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenDash");
    public int RetentionDays { get; set; } = 7;
    public int MaxStorageGb { get; set; } = 50;
    public int MinFreeSpaceGb { get; set; } = 2;
    public int SegmentMinutes { get; set; } = 5;
    public bool SingleFile { get; set; } = true;
    public int Fps { get; set; } = 15;
    public int VideoBitrateKbps { get; set; } = 3000;
    public string EncoderMode { get; set; } = "Auto";
    public bool AllowSoftwareEncoding { get; set; }
    public bool CaptureCursor { get; set; } = true;
    public bool AudioEnabled { get; set; } = true;
    public string AudioDeviceId { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public bool StartWithWindows { get; set; }
    public bool RecordOnLaunch { get; set; }
    public bool PauseOnLock { get; set; } = true;
    public int ProtectMinutes { get; set; } = 10;
    public bool ShowNotifications { get; set; } = true;
    public bool MinimizeOnRecord { get; set; }
    public bool AnimatedBackground { get; set; } = true;
    public int AutoStopMinutes { get; set; }
    public bool MicrophoneEnabled { get; set; }
    public string MicrophoneDeviceId { get; set; } = "";
    public int MicrophoneGainPercent { get; set; } = 100;
    public int SystemGainPercent { get; set; } = 100;
    public bool GameDetectionEnabled { get; set; }
    public List<string> GameExecutables { get; set; } = [];
    public bool WarnOnSilence { get; set; } = true;
    public int SilenceWarningSeconds { get; set; } = 30;
    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.GameExecutables = GameExecutables?.ToList() ?? [];
        return copy;
    }
}

public sealed record DisplaySource(string Id, string Name, int AdapterIndex, int OutputIndex, int X, int Y, int Width, int Height)
{
    public override string ToString() => Name;
}
public sealed record AudioSource(string Id, string Name)
{
    public override string ToString() => Name;
}
public sealed record RecordingInfo(string Path, DateTime StartedUtc, long Bytes, bool Protected, bool Active, string Label = "")
{
    public string FileName => System.IO.Path.GetFileName(Path);
}
public sealed record CleanupResult(int DeletedCount, long DeletedBytes, long TotalBytes, long ProtectedBytes, long FreeBytes, bool CanRecord, string Message);
public sealed record RecorderStatus(bool Recording, string Encoder, string Message, double Fps, long Frames, DateTime? StartedUtc, double AudioPeak = 0, string AudioDevice = "", double MicrophonePeak = 0, string MicrophoneDevice = "", string AudioWarning = "", TimeSpan VideoElapsed = default);
public sealed record DashboardStatus(bool Recording, bool Busy, string Message, string Encoder, TimeSpan Elapsed, double CpuPercent, long MemoryBytes, double Fps, CleanupResult? Storage, bool Armed = false, bool Paused = false, string PauseReason = "", double AudioPeak = 0, string AudioDevice = "", TimeSpan? Remaining = null, double MicrophonePeak = 0, string MicrophoneDevice = "", string AudioWarning = "");
public sealed record MediaDetails(TimeSpan Duration, int Width, int Height, bool HasAudio);
public sealed record ClipRange(TimeSpan Start, TimeSpan Duration);
public sealed record RecordingBookmark(string Id, TimeSpan Offset, DateTime CreatedUtc, string Label);
public sealed record RecordingTimeMatch(RecordingInfo Recording, TimeSpan Offset);
public sealed record RecordingProfile(string Id, string Name, AppSettings Settings);
public sealed record UpdateInfo(string Version, bool IsNewer, string ReleaseUrl, string DownloadUrl, string ChecksumUrl, long Size);
public sealed record PreparedUpdate(string Version, string Directory, string ExecutablePath);

public interface IAppController : IDisposable
{
    AppSettings Settings { get; }
    IReadOnlyList<DisplaySource> Displays { get; }
    IReadOnlyList<AudioSource> AudioDevices { get; }
    IReadOnlyList<AudioSource> Microphones { get; }
    DashboardStatus Status { get; }
    event Action? Changed;
    event Action<string>? Notice;
    Task ToggleRecordingAsync();
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task SaveSettingsAsync(AppSettings settings);
    Task RefreshDevicesAsync();
    Task ProtectRecentAsync();
    IReadOnlyList<RecordingInfo> GetRecordings();
    Task<IReadOnlyList<RecordingInfo>> GetRecordingsAsync(CancellationToken cancellationToken = default);
    Task SetProtectedAsync(string path, bool protect);
    Task SetProtectedManyAsync(IReadOnlyList<string> paths, bool protect);
    Task SetLabelAsync(string path, string label);
    Task<int> DeleteRecordingsAsync(IReadOnlyList<string> paths, bool allowProtected = false);
    Task ExportAsync(string path, string destination, CancellationToken cancellationToken = default);
    Task<MediaDetails> GetMediaDetailsAsync(string path, CancellationToken cancellationToken = default);
    Task ExportClipAsync(string path, string destination, ClipRange range, CancellationToken cancellationToken = default);
    Task AddBookmarkAsync(string label = "");
    Task<IReadOnlyList<RecordingBookmark>> GetBookmarksAsync(string path, CancellationToken cancellationToken = default);
    Task RenameBookmarkAsync(string path, string id, string label);
    Task DeleteBookmarkAsync(string path, string id);
    Task<RecordingTimeMatch?> FindByTimeAsync(DateTime localTime, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecordingProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);
    Task SaveProfileAsync(string name, AppSettings settings);
    Task ApplyProfileAsync(string id);
    Task DeleteProfileAsync(string id);
    Task<IReadOnlyList<string>> GetRunningGamesAsync(CancellationToken cancellationToken = default);
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<PreparedUpdate> PrepareUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    void LaunchPreparedUpdate(PreparedUpdate update);
    void OpenRecordingFolder();
    void OpenLogs();
}
