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
    public int AutoStopMinutes { get; set; }
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
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
public sealed record RecorderStatus(bool Recording, string Encoder, string Message, double Fps, long Frames, DateTime? StartedUtc, double AudioPeak = 0, string AudioDevice = "");
public sealed record DashboardStatus(bool Recording, bool Busy, string Message, string Encoder, TimeSpan Elapsed, double CpuPercent, long MemoryBytes, double Fps, CleanupResult? Storage, bool Armed = false, bool Paused = false, string PauseReason = "", double AudioPeak = 0, string AudioDevice = "", TimeSpan? Remaining = null);
public sealed record MediaDetails(TimeSpan Duration, int Width, int Height, bool HasAudio);
public sealed record ClipRange(TimeSpan Start, TimeSpan Duration);

public interface IAppController : IDisposable
{
    AppSettings Settings { get; }
    IReadOnlyList<DisplaySource> Displays { get; }
    IReadOnlyList<AudioSource> AudioDevices { get; }
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
    void OpenRecordingFolder();
    void OpenLogs();
}
