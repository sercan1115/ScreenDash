using System.Diagnostics;

namespace ScreenDash;

public sealed record GameProcess(string ExecutableName, int SessionId);
public sealed record GameDetectionResult(bool HasGames, bool ShouldStart, bool ShouldStop, IReadOnlyList<string> RunningGames);

/// <summary>Small process-name snapshots, sampled by the controller only when the user enables detection.</summary>
public sealed class GameDetection
{
    private readonly Func<IReadOnlyList<GameProcess>> _snapshot;
    private readonly int _sessionId;
    private readonly object _stateGate = new();
    private long _stateRevision;
    private bool _ownsRecording;
    private bool _suppressed;

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
        var configured = NormalizeNames(executableNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var running = configured.Count == 0 ? [] : _snapshot()
            .Where(process => process.SessionId == _sessionId)
            .Select(process => TryNormalize(process.ExecutableName))
            .Where(name => name is not null && configured.Contains(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        bool hasGames = running.Length != 0;
        // Stop means Stop: an explicitly stopped recording cannot be restarted until all selected games exit.
        lock (_stateGate)
        {
            // Process enumeration holds no control lock; Stop stays immediate even on a slow snapshot.
            // A snapshot predating Stop must not clear its suppression.
            if (!hasGames && revision == _stateRevision) _suppressed = false;
            return new(hasGames, hasGames && !_suppressed && !_ownsRecording, !hasGames && _ownsRecording, running);
        }
    }

    public void MarkRecordingStarted() { lock (_stateGate) { _ownsRecording = true; _stateRevision++; } }
    public void MarkRecordingStopped() { lock (_stateGate) { _ownsRecording = false; _stateRevision++; } }
    public void SuppressUntilGamesExit() { lock (_stateGate) { _suppressed = true; _stateRevision++; } }
    public void Reset() { lock (_stateGate) { _ownsRecording = false; _suppressed = false; _stateRevision++; } }

    public IReadOnlyList<string> GetRunningPrograms() => _snapshot()
        .Where(process => process.SessionId == _sessionId)
        .Select(process => TryNormalize(process.ExecutableName))
        .Where(name => name is not null)
        .Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

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
        var result = new List<GameProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == _sessionId)
                        result.Add(new(process.ProcessName, _sessionId));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                { /* An exiting or inaccessible process is absent from this snapshot. */ }
            }
        }
        return result;
    }
}
