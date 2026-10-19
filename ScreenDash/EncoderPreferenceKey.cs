namespace ScreenDash;

/// <summary>A bounded, in-memory preference is valid only for the same executable and capture configuration.</summary>
internal sealed record EncoderPreferenceKey(string ExecutablePath, long ExecutableLength, long ExecutableModifiedUtc,
    string DisplayId, int Adapter, int Output, int X, int Y, int Width, int Height, int Fps, string Quality, int Bitrate)
{
    internal static EncoderPreferenceKey? Create(string executable, AppSettings settings, DisplaySource display)
    {
        try
        {
            var file = new FileInfo(Path.GetFullPath(executable));
            if (!file.Exists) return null;
            return new(file.FullName.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc.Ticks,
                display.Id, display.AdapterIndex, display.OutputIndex, display.X, display.Y, display.Width, display.Height,
                settings.Fps, RecordingQuality.NormalizeQuality(settings.EncodingQuality), RecordingQuality.ResolveBitrateKbps(settings, display));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A preference is optional. The normal fresh probe still reports an inaccessible executable.
            return null;
        }
    }
}
