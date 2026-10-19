using System.Text.Json;
using System.Text;

namespace ScreenDash;

public static class SettingsStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string FilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenDash", "settings.json");

    public static AppSettings Load() => Load(FilePath);

    public static AppSettings Load(string filePath)
    {
        lock (Gate)
        {
            if (!File.Exists(filePath)) return Validate(new AppSettings());
            try
            {
                var info = new FileInfo(filePath);
                if (info.Length > 1024 * 1024) throw new JsonException("Die Einstellungsdatei ist zu groß.");
                using var json = JsonDocument.Parse(File.ReadAllText(filePath, Encoding.UTF8));
                var settings = json.RootElement.Deserialize<AppSettings>(JsonOptions);
                if (settings is not null) ApplyLegacyQualityDefaults(settings, json.RootElement);
                return Validate(settings ?? throw new JsonException("Die Einstellungsdatei ist leer."));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                AppLog.Write("Ungültige Einstellungen; Standardwerte werden verwendet. " + ex.Message);
                try
                {
                    // Keep the original recoverable rather than overwriting a malformed file.
                    if (File.Exists(filePath)) File.Move(filePath, filePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8]);
                }
                catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { AppLog.Write(moveError); }
                return Validate(new AppSettings());
            }
        }
    }

    public static void Save(AppSettings settings) => Save(settings, FilePath);

    public static void Save(AppSettings settings, string filePath)
    {
        var validated = Validate(settings);
        var destination = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Ungültiger Pfad für die Einstellungen.", nameof(filePath));
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, validated, JsonOptions);
                    stream.Flush(true);
                }
                // Move/replace on the same volume leaves either the old complete file or the new complete file.
                File.Move(temporary, destination, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Einstellungen konnten nicht gespeichert werden. Bitte Schreibrechte und freien Speicher prüfen.", ex);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
            }
        }
    }

    public static AppSettings Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var value = settings.Clone();
        if (string.IsNullOrWhiteSpace(value.RecordingDirectory))
            throw new ArgumentException("Bitte einen Aufnahmeordner auswählen.", nameof(settings));
        if (!Path.IsPathFullyQualified(value.RecordingDirectory))
            throw new ArgumentException("Bitte einen vollständigen Aufnahmeordner angeben, zum Beispiel C:\\Videos\\ScreenDash.", nameof(settings));
        try { value.RecordingDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.RecordingDirectory.Trim())); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Der Aufnahmeordner ist ungültig. Bitte einen anderen Ordner auswählen.", nameof(settings), ex);
        }
        if (value.RecordingDirectory.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Bitte einen lokalen Aufnahmeordner wählen. Netzlaufwerke unterstützen die sichere Speicherprüfung nicht.", nameof(settings));
        value.RetentionDays = Math.Clamp(value.RetentionDays, 1, 365);
        value.MaxStorageGb = Math.Clamp(value.MaxStorageGb, 1, 4096);
        value.MinFreeSpaceGb = Math.Clamp(value.MinFreeSpaceGb, 1, 128);
        value.SegmentMinutes = Math.Clamp(value.SegmentMinutes, 1, 60);
        value.Fps = Math.Clamp(value.Fps, 5, 60);
        value.VideoBitrateKbps = Math.Clamp(value.VideoBitrateKbps, 500, RecordingQuality.MaximumBitrateKbps);
        value.EncodingQuality = RecordingQuality.NormalizeQuality(value.EncodingQuality);
        value.ProtectMinutes = Math.Clamp(value.ProtectMinutes, 1, 180);
        value.AutoStopMinutes = Math.Clamp(value.AutoStopMinutes, 0, 1440);
        value.MicrophoneDeviceId ??= "";
        value.MicrophoneGainPercent = Math.Clamp(value.MicrophoneGainPercent, 0, 200);
        value.SystemGainPercent = Math.Clamp(value.SystemGainPercent, 0, 200);
        value.SilenceWarningSeconds = Math.Clamp(value.SilenceWarningSeconds, 10, 600);
        value.GameExecutables = GameDetection.NormalizeNames(value.GameExecutables ?? []);
        // Game-only automation must never be bypassed by an older record-on-launch preference.
        if (value.GameDetectionEnabled) value.RecordOnLaunch = false;
        value.EncoderMode = value.EncoderMode?.Trim() ?? "Auto";
        if (value.EncoderMode is not ("Auto" or "NVIDIA" or "AMD" or "Intel" or "Software"))
            throw new ArgumentException("Bitte einen gültigen Encoder auswählen: Auto, NVIDIA, AMD, Intel oder Software.", nameof(settings));
        value.AudioDeviceId ??= "";
        value.DisplayId ??= "";
        return value;
    }

    internal static void ApplyLegacyQualityDefaults(AppSettings settings, JsonElement stored)
    {
        // Existing explicit bitrate choices keep their meaning; the new automatic mode is opt-in on migration.
        if (stored.ValueKind == JsonValueKind.Object && !stored.EnumerateObject().Any(property =>
            property.Name.Equals(nameof(AppSettings.AutoVideoBitrate), StringComparison.OrdinalIgnoreCase)))
            settings.AutoVideoBitrate = false;
    }
}
