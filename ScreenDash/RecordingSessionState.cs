namespace ScreenDash;

/// <summary>Capture intent independent of devices. Manual and privacy pauses are kept separately.</summary>
public sealed class RecordingSessionState
{
    private readonly object _gate = new();
    private bool _armed, _manual, _locked, _suspended, _pauseOnLock = true, _shutdown;
    private long _generation;
    public bool Armed { get { lock (_gate) return _armed; } }
    public bool ManualPaused { get { lock (_gate) return _manual; } }
    public bool Paused { get { lock (_gate) return PausedCore; } }
    public bool CanCapture { get { lock (_gate) return CanCaptureCore; } }
    public long Generation { get { lock (_gate) return _generation; } }
    public string PauseReason
    {
        get
        {
            lock (_gate)
            {
                if (!_armed) return "";
                if (_manual) return "Manuell pausiert";
                if (_suspended) return "Energiesparmodus";
                if (_pauseOnLock && _locked) return "PC gesperrt";
                return "";
            }
        }
    }
    private bool PausedCore => _armed && (_manual || _suspended || (_pauseOnLock && _locked));
    private bool CanCaptureCore => _armed && !_shutdown && !PausedCore;

    public void Start()
    {
        lock (_gate)
        {
            if (_shutdown) throw new InvalidOperationException("Die App wird beendet.");
            if (_locked || _suspended) throw new InvalidOperationException("Entsperre den PC, um die Aufnahme zu starten.");
            _armed = true; _manual = false; _generation++;
        }
    }
    public void Stop() { lock (_gate) { _armed = false; _manual = false; _generation++; } }
    public void Pause()
    {
        lock (_gate)
        {
            if (!_armed || _shutdown) throw new InvalidOperationException("Es ist keine Aufnahme aktiv.");
            _manual = true; _generation++;
        }
    }
    public void Resume()
    {
        lock (_gate)
        {
            if (!_armed || _shutdown) throw new InvalidOperationException("Es ist keine pausierte Aufnahme vorhanden.");
            if (!PausedCore) return;
            _manual = false; _generation++;
        }
    }
    public void SetPrivacy(bool locked, bool suspended, bool pauseOnLock)
    {
        lock (_gate)
        {
            if (_locked == locked && _suspended == suspended && _pauseOnLock == pauseOnLock) return;
            _locked = locked; _suspended = suspended; _pauseOnLock = pauseOnLock; _generation++;
        }
    }
    public bool CanRecover(long generation) { lock (_gate) return generation == _generation && CanCaptureCore; }
    public void Shutdown() { lock (_gate) { _shutdown = true; _armed = false; _manual = false; _generation++; } }
}
