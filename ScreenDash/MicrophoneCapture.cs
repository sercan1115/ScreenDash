using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace ScreenDash;

/// <summary>Optional WASAPI microphone stream with the same bounded ring and silent gaps as loopback.</summary>
public sealed class MicrophoneCapture : IDisposable
{
    private readonly AudioLoopback _capture;
    public event Action<Exception>? Faulted
    {
        add => _capture.Faulted += value;
        remove => _capture.Faulted -= value;
    }
    public int SampleRate => _capture.SampleRate;
    public int Channels => _capture.Channels;
    public string RawFormat => _capture.RawFormat;
    public string PipePath => _capture.PipePath;
    public string DeviceName => _capture.DeviceName;
    public double PeakLevel => _capture.PeakLevel;

    public static IReadOnlyList<AudioSource> GetDevices()
    {
        var result = new List<AudioSource> { new("", "Windows-Standardmikrofon · Kommunikation") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (endpoint) result.Add(new AudioSource(endpoint.ID, endpoint.FriendlyName));
        }
        return result;
    }

    public MicrophoneCapture(string deviceId) => _capture = new AudioLoopback(deviceId, true);
    public void Start(Stopwatch? sharedClock = null) => _capture.Start(sharedClock);
    public Task StopAsync() => _capture.StopAsync();
    public void Dispose() => _capture.Dispose();
}
