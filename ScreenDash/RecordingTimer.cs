namespace ScreenDash;

/// <summary>Optional limit on recorded time; the caller's monotonic recording clock excludes pauses.</summary>
public static class RecordingTimer
{
    public static TimeSpan? Remaining(int minutes, TimeSpan elapsed, bool armed)
    {
        if (minutes <= 0 || !armed) return null;
        var remaining = TimeSpan.FromMinutes(Math.Min(minutes, 1440)) - (elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
    public static bool HasExpired(int minutes, TimeSpan elapsed) => minutes > 0 && Remaining(minutes, elapsed, true) == TimeSpan.Zero;
}
