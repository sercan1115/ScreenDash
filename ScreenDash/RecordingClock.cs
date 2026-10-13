using System.Diagnostics;

namespace ScreenDash;

/// <summary>Counts actual recording intervals using a monotonic clock; pauses and system-time changes do not add time.</summary>
internal sealed class RecordingClock
{
    private readonly object _gate = new();
    private readonly Func<long> _timestamp;
    private readonly long _frequency;
    private TimeSpan _completed;
    private long? _started;

    internal RecordingClock(Func<long>? timestamp = null, long? frequency = null)
    {
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _frequency = frequency ?? Stopwatch.Frequency;
        if (_frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
    }

    public TimeSpan Elapsed { get { lock (_gate) return _completed + CurrentInterval(); } }
    public void Begin() { lock (_gate) _started ??= _timestamp(); }
    public void End()
    {
        lock (_gate)
        {
            _completed += CurrentInterval();
            _started = null;
        }
    }
    public void Reset() { lock (_gate) { _completed = TimeSpan.Zero; _started = null; } }
    private TimeSpan CurrentInterval() => _started is long start
        ? TimeSpan.FromSeconds(Math.Max(0, (_timestamp() - start) / (double)_frequency))
        : TimeSpan.Zero;
}
