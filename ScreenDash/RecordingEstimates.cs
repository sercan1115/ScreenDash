namespace ScreenDash;

public sealed record RecordingEstimate(double BytesPerHour, double GiBPerHour, double HoursAtLimit, double EffectiveRetentionHours, double GiBForFullRetention, bool FullRetentionFits);

/// <summary>Planning estimates from nominal bitrate, including 3% container margin; not measured sizes.</summary>
public static class RecordingEstimates
{
    public static RecordingEstimate Calculate(AppSettings settings, long protectedBytes = 0, long? freeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        const double gib = 1024d * 1024 * 1024;
        double rate = (Math.Clamp(settings.VideoBitrateKbps, 500, 50_000) + (settings.AudioEnabled || settings.MicrophoneEnabled ? 128 : 0)) * 1000d / 8 * 3600 * 1.03;
        double pinned = Math.Max(0, protectedBytes);
        double quota = Math.Clamp(settings.MaxStorageGb, 1, 4096) * gib;
        double hoursAtLimit = Math.Max(0, quota - pinned) / rate;
        double hoursAtFree = freeBytes.HasValue ? Math.Max(0, freeBytes.Value - Math.Clamp(settings.MinFreeSpaceGb, 1, 128) * gib) / rate : double.PositiveInfinity;
        double requestedHours = Math.Clamp(settings.RetentionDays, 1, 365) * 24d;
        double effective = Math.Min(requestedHours, Math.Min(hoursAtLimit, hoursAtFree));
        double needed = (requestedHours * rate + pinned) / gib;
        return new(rate, rate / gib, hoursAtLimit, effective, needed, hoursAtLimit >= requestedHours && hoursAtFree >= requestedHours);
    }
}
