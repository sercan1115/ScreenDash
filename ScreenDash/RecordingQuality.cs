namespace ScreenDash;

/// <summary>H.264 bitrate guidance for the native capture size, with a bounded disk and encoder budget.</summary>
public static class RecordingQuality
{
    public const int MaximumBitrateKbps = 100_000;

    public static string NormalizeQuality(string? quality) => quality?.Trim() switch
    {
        null or "" => "Balanced",
        "Efficiency" => "Efficiency",
        "Balanced" => "Balanced",
        "Quality" => "Quality",
        _ => throw new ArgumentException("Bitte eine gültige Videoqualität auswählen: Effizient, Ausgewogen oder Hohe Qualität.", nameof(quality))
    };

    public static int RecommendedBitrateKbps(int width, int height, int fps, string quality = "Balanced")
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Die Bildschirmbreite muss größer als null sein.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Die Bildschirmhöhe muss größer als null sein.");
        if (fps is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(fps), "Die Bildrate muss zwischen 1 und 60 liegen.");
        var baseline = NormalizeQuality(quality) switch
        {
            "Efficiency" => 6_000d,
            "Quality" => 20_000d,
            _ => 10_000d
        };
        // Larger frames and faster movement need more data; sub-linear scaling avoids
        // multiplying disk usage directly with every extra pixel or frame.
        var pixels = (double)width * height / (1920d * 1080);
        var recommended = baseline * Math.Pow(pixels, 0.85) * Math.Pow(fps / 30d, 0.75);
        return (int)Math.Clamp(Math.Round(recommended / 500) * 500, 1_000, MaximumBitrateKbps);
    }

    public static int ResolveBitrateKbps(AppSettings settings, DisplaySource display)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(display);
        return settings.AutoVideoBitrate
            ? RecommendedBitrateKbps(display.Width, display.Height, settings.Fps, settings.EncodingQuality)
            : Math.Clamp(settings.VideoBitrateKbps, 500, MaximumBitrateKbps);
    }

    public static int PeakBitrateKbps(int targetKbps)
        => (int)Math.Clamp((long)targetKbps * 3 / 2, 500, MaximumBitrateKbps);
}
