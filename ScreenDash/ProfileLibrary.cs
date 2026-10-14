using System.Text;
using System.Text.Json;

namespace ScreenDash;

/// <summary>User-defined capture presets. Applying one never changes retention, storage, or automation.</summary>
public sealed class ProfileLibrary
{
    private const int MaximumProfiles = 30;
    private const long MaximumJsonBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _filePath;

    public ProfileLibrary(string? filePath = null) => _filePath = Path.GetFullPath(filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenDash", "profiles.json"));

    public IReadOnlyList<RecordingProfile> Load()
    {
        lock (Gate) return LoadCore().Select(CloneProfile).ToArray();
    }

    public RecordingProfile Save(string name, AppSettings settings, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        name = ValidateName(name);
        lock (Gate)
        {
            var profiles = LoadCore();
            int index = id is null ? -1 : profiles.FindIndex(profile => profile.Id == ValidateId(id));
            if (id is not null && index < 0) throw new ArgumentException("Das gewählte Aufnahmeprofil existiert nicht mehr.", nameof(id));
            if (index < 0 && profiles.Count >= MaximumProfiles) throw new InvalidOperationException("Es sind höchstens 30 Aufnahmeprofile möglich.");
            var profile = new RecordingProfile(id ?? Guid.NewGuid().ToString("N"), name, SettingsStore.Validate(settings));
            if (index < 0) profiles.Add(profile); else profiles[index] = profile;
            SaveCore(profiles);
            return CloneProfile(profile);
        }
    }

    public void Delete(string id)
    {
        id = ValidateId(id);
        lock (Gate)
        {
            var profiles = LoadCore();
            if (profiles.RemoveAll(profile => profile.Id == id) == 0)
                throw new ArgumentException("Das gewählte Aufnahmeprofil existiert nicht mehr.", nameof(id));
            SaveCore(profiles);
        }
    }

    public static AppSettings Apply(AppSettings current, RecordingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var destination = SettingsStore.Validate(current);
        var source = SettingsStore.Validate(profile.Settings);
        destination.DisplayId = source.DisplayId;
        destination.AudioEnabled = source.AudioEnabled;
        destination.AudioDeviceId = source.AudioDeviceId;
        destination.Fps = source.Fps;
        destination.VideoBitrateKbps = source.VideoBitrateKbps;
        destination.EncoderMode = source.EncoderMode;
        destination.AllowSoftwareEncoding = source.AllowSoftwareEncoding;
        destination.CaptureCursor = source.CaptureCursor;
        destination.SingleFile = source.SingleFile;
        destination.SegmentMinutes = source.SegmentMinutes;
        destination.MicrophoneEnabled = source.MicrophoneEnabled;
        destination.MicrophoneDeviceId = source.MicrophoneDeviceId;
        destination.MicrophoneGainPercent = source.MicrophoneGainPercent;
        destination.SystemGainPercent = source.SystemGainPercent;
        return SettingsStore.Validate(destination);
    }

    private List<RecordingProfile> LoadCore()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumJsonBytes) throw new JsonException("Die Profildatei ist zu groß.");
            var profiles = JsonSerializer.Deserialize<List<RecordingProfile>>(stream, JsonOptions) ?? throw new JsonException("Die Profildatei ist leer.");
            if (profiles.Count > MaximumProfiles) throw new JsonException("Die Profildatei enthält zu viele Profile.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var normalized = new List<RecordingProfile>(profiles.Count);
            foreach (var profile in profiles)
            {
                if (profile is null || profile.Settings is null) throw new JsonException("Das Aufnahmeprofil ist unvollständig.");
                var id = ValidateId(profile.Id);
                if (!ids.Add(id)) throw new JsonException("Doppelte Profilkennung.");
                normalized.Add(new(id, ValidateName(profile.Name), SettingsStore.Validate(profile.Settings)));
            }
            return normalized;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        { throw new IOException("Die Aufnahmeprofile konnten nicht gelesen werden. Die vorhandene Datei bleibt zur Wiederherstellung erhalten.", ex); }
    }

    private void SaveCore(IReadOnlyList<RecordingProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".profiles-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, profiles, JsonOptions);
                if (stream.Length > MaximumJsonBytes) throw new IOException("Die Aufnahmeprofile sind zu groß.");
                stream.Flush(true);
            }
            File.Move(temporary, _filePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ValidateName(string name)
    {
        var value = name?.Trim() ?? "";
        if (value.Length == 0 || value.EnumerateRunes().Count() > 60 || value.Any(char.IsControl))
            throw new ArgumentException("Ein Profilname muss zwischen 1 und 60 Zeichen lang sein.", nameof(name));
        return value;
    }

    private static string ValidateId(string id) => Guid.TryParseExact(id, "N", out var value) && value != Guid.Empty
        ? value.ToString("N") : throw new ArgumentException("Ungültige Profilkennung.", nameof(id));

    private static RecordingProfile CloneProfile(RecordingProfile profile) => profile with { Settings = profile.Settings.Clone() };
}
