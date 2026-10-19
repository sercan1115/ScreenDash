using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenDash;

public sealed record GameProcess(string ExecutableName, int SessionId, bool HasVisibleWindow = true, bool IsForeground = true, int ProcessId = 0);
public sealed record GameDetectionResult(bool HasGames, bool ShouldStart, bool ShouldStop, IReadOnlyList<string> RunningGames,
    bool SnapshotReliable = true, string Diagnostic = "");

/// <summary>Exact process names plus visible foreground windows; sampled only when detection is enabled.</summary>
public sealed class GameDetection
{
    // These hosts remain open between game sessions. Selecting one must never start capture.
    private static readonly HashSet<string> ExcludedPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "gameoverlayui.exe", "epicgameslauncher.exe", "epicwebhelper.exe",
        "battle.net.exe", "agent.exe", "riotclientservices.exe", "riotclientux.exe", "riotclientuxrender.exe",
        "ubisoftconnect.exe", "upc.exe", "uplay.exe", "eadesktop.exe", "ealauncher.exe", "eabackgroundservice.exe",
        "origin.exe", "originwebhelperservice.exe", "galaxyclient.exe", "galaxyclient helper.exe",
        "rockstargameslauncher.exe", "launcher.exe", "playnite.desktopapp.exe", "playnite.fullscreenapp.exe",
        "screendash.exe"
    };
    private readonly Func<IReadOnlyList<GameProcess>> _snapshot;
    private readonly int _sessionId;
    private readonly object _stateGate = new();
    private long _stateRevision;
    private bool _ownsRecording;
    private bool _suppressed;
    private string _lastDiagnostic = "Warte auf ein ausgewähltes Spiel im Vordergrund.";

    public string LastDiagnostic { get { lock (_stateGate) return _lastDiagnostic; } }

    public GameDetection(Func<IReadOnlyList<GameProcess>>? snapshot = null, int? currentSessionId = null)
    {
        _snapshot = snapshot ?? SnapshotCurrentSession;
        using var current = Process.GetCurrentProcess();
        _sessionId = currentSessionId ?? current.SessionId;
    }

    public GameDetectionResult Evaluate(IEnumerable<string> executableNames)
    {
        ArgumentNullException.ThrowIfNull(executableNames);
        long revision; lock (_stateGate) revision = _stateRevision;
        var normalized = NormalizeNames(executableNames);
        var configured = normalized.Where(name => !ExcludedPrograms.Contains(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignored = normalized.Where(ExcludedPrograms.Contains).ToArray();
        IReadOnlyList<GameProcess> snapshot;
        try { snapshot = configured.Count == 0 ? [] : _snapshot(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or NotSupportedException)
        {
            // Unknown is neither a game exit nor permission to clear a user's explicit Stop.
            lock (_stateGate)
            {
                _lastDiagnostic = "Spielerkennung vorübergehend nicht verfügbar. Die Aufnahme bleibt unverändert.";
                return new(false, false, false, [], false, _lastDiagnostic);
            }
        }

        var matching = snapshot.Where(process => process.SessionId == _sessionId)
            .Select(process => (Process: process, Name: TryNormalize(process.ExecutableName)))
            .Where(item => item.Name is not null && configured.Contains(item.Name)).ToArray();
        var running = matching.Select(item => item.Name!).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        bool hasGames = running.Length != 0;
        bool foregroundGame = matching.Any(item => item.Process.HasVisibleWindow && item.Process.IsForeground);
        lock (_stateGate)
        {
            // Enumeration holds no control lock: Stop stays immediate, even on a slow snapshot.
            // A snapshot predating Stop must not clear its suppression.
            if (!hasGames && revision == _stateRevision) _suppressed = false;
            string ignoredHint = ignored.Length == 0 ? "" : " Launcher ignoriert: " + string.Join(", ", ignored) + ".";
            _lastDiagnostic = configured.Count == 0
                ? (ignored.Length == 0 ? "Keine Spiele ausgewählt." : "Wähle die EXE des Spiels statt seines Launchers.") + ignoredHint
                : _suppressed && hasGames ? "Manuell gestoppt · erst nach dem Beenden aller ausgewählten Spiele wieder bereit." + ignoredHint
                : _ownsRecording && hasGames ? "Spielaufnahme aktiv · Alt+Tab unterbricht sie nicht." + ignoredHint
                : _ownsRecording ? "Spiel beendet · die Aufnahme stoppt nach 10 Sekunden." + ignoredHint
                : foregroundGame ? "Spiel im Vordergrund erkannt · " + string.Join(", ", running) + "." + ignoredHint
                : hasGames ? "Prozess läuft im Hintergrund · für den Start das Spielfenster öffnen und aktivieren." + ignoredHint
                : "Warte auf ein ausgewähltes Spiel im Vordergrund." + ignoredHint;
            return new(hasGames, foregroundGame && !_suppressed && !_ownsRecording, !hasGames && _ownsRecording, running, true, _lastDiagnostic);
        }
    }

    public void MarkRecordingStarted() { lock (_stateGate) { _ownsRecording = true; _stateRevision++; } }
    public void MarkRecordingStopped() { lock (_stateGate) { _ownsRecording = false; _stateRevision++; } }
    public void SuppressUntilGamesExit() { lock (_stateGate) { _suppressed = true; _stateRevision++; } }
    public void Reset()
    {
        lock (_stateGate)
        {
            _ownsRecording = false; _suppressed = false; _stateRevision++;
            _lastDiagnostic = "Warte auf ein ausgewähltes Spiel im Vordergrund.";
        }
    }

    public IReadOnlyList<string> GetRunningPrograms() => _snapshot()
        .Where(process => process.SessionId == _sessionId && process.HasVisibleWindow)
        .Select(process => TryNormalize(process.ExecutableName))
        .Where(name => name is not null && !ExcludedPrograms.Contains(name))
        .Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsExcludedExecutable(string value) => ExcludedPrograms.Contains(NormalizeExecutableName(value));

    public static List<string> NormalizeNames(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var value in values)
        {
            if (++count > 30) throw new ArgumentException("Es sind höchstens 30 Programme für die Spielerkennung möglich.");
            result.Add(NormalizeExecutableName(value));
        }
        return result.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string NormalizeExecutableName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Bitte einen Programmnamen wie game.exe angeben.");
        var name = value.Trim();
        if (name.Length > 128 || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || name.Any(char.IsControl))
            throw new ArgumentException("Für die Spielerkennung nur einen Programmnamen angeben, zum Beispiel game.exe.");
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("Ungültiger Programmname für die Spielerkennung.");
        return name.ToLowerInvariant() + ".exe";
    }

    private static string? TryNormalize(string value)
    {
        try { return NormalizeExecutableName(value); }
        catch (ArgumentException) { return null; }
    }

    private IReadOnlyList<GameProcess> SnapshotCurrentSession()
    {
        var windows = VisibleWindowProcesses();
        GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundProcessId);
        var result = new List<GameProcess>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId == _sessionId)
                        result.Add(new(process.ProcessName, _sessionId, windows.Contains(process.Id), process.Id == foregroundProcessId, process.Id));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // An inaccessible visible application is unknown, not an observed game exit.
                    if (windows.Contains(process.Id) && !process.HasExited) throw new IOException("Ein Fensterprozess konnte nicht geprüft werden.", ex);
                }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return result;
    }

    private static HashSet<int> VisibleWindowProcesses()
    {
        var processIds = new HashSet<int>();
        EnumWindowsCallback callback = (window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero || (GetWindowLong(window, -20) & 0x80) != 0
                || !GetWindowRect(window, out var rectangle) || rectangle.Right - rectangle.Left < 160 || rectangle.Bottom - rectangle.Top < 90)
                return true;
            // Windows on another virtual desktop are technically visible but cloaked by DWM.
            if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != 0) processIds.Add(checked((int)processId));
            return true;
        };
        if (!EnumWindows(callback, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return processIds;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out WindowRectangle rectangle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
