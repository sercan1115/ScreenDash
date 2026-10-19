using System.Diagnostics;

namespace ScreenDash;

/// <summary>Recent delivered frames per wall-clock second, without allocating or scheduling a timer.</summary>
internal sealed class RecentFrameRate
{
    private readonly record struct Sample(long Frames, long Counter);
    private readonly Sample[] _samples = new Sample[64];
    private readonly double _maximumFps;
    private int _first, _count;
    private double _lastFps;

    internal RecentFrameRate(int maximumFps)
    {
        if (maximumFps is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(maximumFps));
        _maximumFps = maximumFps;
    }

    internal double Update(long frames, long counter, double startupFps)
    {
        frames = Math.Max(0, frames);
        if (_count > 0)
        {
            var previous = At(_count - 1);
            if (counter < previous.Counter || frames < previous.Frames) { _first = 0; _count = 0; }
            else if (counter == previous.Counter) return _lastFps;
        }
        if (_count == _samples.Length) { _first = (_first + 1) % _samples.Length; _count--; }
        _samples[(_first + _count++) % _samples.Length] = new(frames, counter);
        var cutoff = counter - 6d * Stopwatch.Frequency;
        // Keep one boundary sample so sparse progress updates still account for a stalled interval.
        while (_count > 2 && At(1).Counter <= cutoff) { _first = (_first + 1) % _samples.Length; _count--; }
        var oldest = At(0);
        var seconds = (counter - (double)oldest.Counter) / Stopwatch.Frequency;
        var fps = seconds >= 0.5 ? (frames - (double)oldest.Frames) / seconds : startupFps;
        return _lastFps = double.IsFinite(fps) ? Math.Clamp(fps, 0, _maximumFps) : 0;
    }

    private Sample At(int index) => _samples[(_first + index) % _samples.Length];
}
