using System.Diagnostics;
using System.IO.Pipes;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenDash;

/// <summary>Playback-device loopback. A bounded, clocked ring also emits silence when WASAPI emits no packets.</summary>
public sealed class AudioLoopback : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _ringLock = new();
    private readonly ClockedAudioBuffer _ring;
    private Stopwatch _clock = new();
    private readonly bool _microphone;
    private readonly string _deviceName;
    private long _lastCaptureEnd;
    private Task? _pump;
    private int _stopping;
    private int _faulted;
    private int _disposed;
    private float _peak;
    private long _lastAudioPacket;

    public event Action<Exception>? Faulted;
    public int SampleRate => _capture.WaveFormat.SampleRate;
    public int Channels => _capture.WaveFormat.Channels;
    public string RawFormat { get; }
    public string PipePath { get; }
    public string DeviceName => _deviceName;
    public double PeakLevel => Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastAudioPacket)).TotalSeconds < 1 ? Volatile.Read(ref _peak) : 0;

    public static IReadOnlyList<AudioSource> GetDevices()
    {
        var result = new List<AudioSource> { new("", "Automatisch · aktive Audioausgabe"), new("@windows-default", "Windows-Standardausgabe") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device) result.Add(new AudioSource(device.ID, device.FriendlyName));
        }
        return result;
    }

    public AudioLoopback(string deviceId) : this(deviceId, false) { }

    // Microphone and loopback share the bounded timeline implementation. Opening a microphone
    // occurs only through MicrophoneCapture, after the user has explicitly enabled it.
    internal AudioLoopback(string deviceId, bool microphone)
    {
        _microphone = microphone;
        using var enumerator = new MMDeviceEnumerator();
        _device = string.IsNullOrEmpty(deviceId)
            ? microphone ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : ResolveAutomaticDevice(enumerator)
            : deviceId == "@windows-default" ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        try
        {
            if (_device.DataFlow != (microphone ? DataFlow.Capture : DataFlow.Render) || _device.State != DeviceState.Active)
                throw new InvalidOperationException(microphone ? "Das ausgewählte Mikrofon ist nicht verfügbar." : "Das ausgewählte Wiedergabegerät ist nicht verfügbar.");
            // Cache the name while the endpoint is valid. Status updates must not query COM
            // properties on a device that may have just been unplugged.
            _deviceName = _device.FriendlyName;
            _capture = microphone ? new WasapiCapture(_device) : new WasapiLoopbackCapture(_device);
            RawFormat = GetRawFormat(_capture.WaveFormat);
            if (SampleRate < 8000 || SampleRate > 192000 || Channels is < 1 or > 16)
                throw new InvalidOperationException(microphone ? "Das Audioformat des Mikrofons wird nicht unterstützt." : "Das Audioformat des Ausgabegerätes wird nicht unterstützt.");
            _ring = new ClockedAudioBuffer(checked(SampleRate * 2), _capture.WaveFormat.BlockAlign);
            var name = "ScreenDash-audio-" + Guid.NewGuid().ToString("N");
            PipePath = @"\\.\pipe\" + name;
            _pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
        }
        catch
        {
            try { _pipe?.Dispose(); } catch { }
            try { _capture?.Dispose(); } catch { }
            try { _device.Dispose(); } catch { }
            _shutdown.Dispose();
            throw;
        }
    }

    public void Start(Stopwatch? sharedClock = null)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_pump is not null) throw new InvalidOperationException("Audioaufnahme läuft bereits.");
        if (sharedClock is not null) _clock = sharedClock;
        _clock.Start();
        _capture.StartRecording();
        _pump = Task.Run(PumpAsync);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_stopping != 0 || e.BytesRecorded == 0) return;
        float peak = 0;
        int sampleBytes = _capture.WaveFormat.BitsPerSample / 8;
        // At most 128 samples per callback: enough for feedback without an audio visualization pipeline.
        int step = Math.Max(1, e.BytesRecorded / Math.Max(1, sampleBytes) / 128) * sampleBytes;
        for (int offset = 0; offset + sampleBytes <= e.BytesRecorded; offset += step)
        {
            float value = RawFormat switch
            {
                "f32le" => Math.Abs(BitConverter.ToSingle(e.Buffer, offset)),
                "s16le" => Math.Abs(BitConverter.ToInt16(e.Buffer, offset) / 32768f),
                "s32le" => Math.Abs(BitConverter.ToInt32(e.Buffer, offset) / 2147483648f),
                "s24le" => Math.Abs(((e.Buffer[offset] | e.Buffer[offset + 1] << 8 | e.Buffer[offset + 2] << 16) << 8) / 2147483648f),
                _ => 0
            };
            if (float.IsFinite(value)) peak = Math.Max(peak, value);
        }
        Volatile.Write(ref _peak, Math.Clamp(peak, 0, 1));
        Interlocked.Exchange(ref _lastAudioPacket, Stopwatch.GetTimestamp());
        var align = _capture.WaveFormat.BlockAlign;
        var frames = e.BytesRecorded / align;
        var end = (long)(_clock.Elapsed.TotalSeconds * SampleRate);
        var start = Math.Max(0, end - frames);
        lock (_ringLock)
        {
            // Smooth callback scheduling jitter, but re-anchor after a genuine silent gap.
            if (_lastCaptureEnd > 0 && Math.Abs(start - _lastCaptureEnd) <= SampleRate / 20)
                start = _lastCaptureEnd;
            _ring.Write(start, e.Buffer, frames);
            _lastCaptureEnd = start + frames;
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping == 0)
            Fail(e.Exception ?? new IOException(_microphone ? "Die Mikrofonaufnahme wurde vom Gerät beendet." : "Die Audioaufnahme wurde vom Ausgabegerät beendet."));
    }

    private async Task PumpAsync()
    {
        var ct = _shutdown.Token;
        try
        {
            await _pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            var chunkFrames = Math.Max(1, SampleRate / 50);
            var align = _capture.WaveFormat.BlockAlign;
            var output = new byte[chunkFrames * align];
            long writtenFrames = 0;
            var nextDeviceCheck = 1.0;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_clock.Elapsed.TotalSeconds >= nextDeviceCheck)
                {
                    nextDeviceCheck = _clock.Elapsed.TotalSeconds + 1;
                    if (_device.State != DeviceState.Active)
                        throw new IOException(_microphone ? "Das ausgewählte Mikrofon wurde getrennt oder deaktiviert." : "Das ausgewählte Audioausgabegerät wurde getrennt oder deaktiviert.");
                }
                // 120 ms lets callback batches arrive before filling a missing time span with silence.
                var due = Math.Max(0, (long)((_clock.Elapsed.TotalSeconds - 0.12) * SampleRate));
                while (writtenFrames < due)
                {
                    ct.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(chunkFrames, due - writtenFrames);
                    _ring.Read(writtenFrames, output, count);
                    await _pipe.WriteAsync(output.AsMemory(0, count * align), ct).ConfigureAwait(false);
                    writtenFrames += count;
                }
            }
        }
        catch (OperationCanceledException) when (_stopping != 0) { }
        catch (ObjectDisposedException) when (_stopping != 0) { }
        catch (Exception ex) { if (_stopping == 0) Fail(ex); }
    }

    private void Fail(Exception error)
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0) return;
        try { Faulted?.Invoke(error); } catch { /* Observers cannot kill the WASAPI callback. */ }
        // A lost endpoint must also close its pipe. Otherwise FFmpeg could remain blocked
        // waiting for that source while the recorder tries to flush the final video packets.
        try { _shutdown.Cancel(); } catch (ObjectDisposedException) { }
        try { _pipe.Dispose(); } catch (IOException) { }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _shutdown.Cancel();
            try { _capture.StopRecording(); } catch { }
            _pipe.Dispose(); // Unblocks a writer even if FFmpeg stopped reading.
        }
        if (_pump is not null)
        {
            try { await _pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
        }
    }

    private static string GetRawFormat(WaveFormat format)
    {
        var encoding = format.Encoding;
        if (format is WaveFormatExtensible extensible)
        {
            if (extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71")) encoding = WaveFormatEncoding.IeeeFloat;
            else if (extensible.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71")) encoding = WaveFormatEncoding.Pcm;
        }
        if (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32) return "f32le";
        if (encoding == WaveFormatEncoding.Pcm)
            return format.BitsPerSample switch
            {
                16 => "s16le", 24 => "s24le", 32 => "s32le",
                _ => throw new InvalidOperationException("Nicht unterstützte PCM-Bittiefe des Audioausgabegerätes.")
            };
        throw new InvalidOperationException("Nicht unterstütztes Audioformat des Ausgabegerätes.");
    }

    private static MMDevice ResolveAutomaticDevice(MMDeviceEnumerator enumerator)
    {
        // Games can use a headset while Windows' multimedia default still points at HDMI.
        // Choose an endpoint with actual playback; when all are silent use the normal default.
        string? activeId = null;
        float highestPeak = 0.0001f;
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                try
                {
                    float peak = endpoint.AudioMeterInformation.MasterPeakValue;
                    if (peak > highestPeak) { highestPeak = peak; activeId = endpoint.ID; }
                }
                catch (System.Runtime.InteropServices.COMException) { }
            }
        }
        return activeId is null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(activeId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { StopAsync().GetAwaiter().GetResult(); }
        finally
        {
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnStopped;
            try { _capture.Dispose(); }
            finally
            {
                try { _device.Dispose(); }
                finally { _shutdown.Dispose(); }
            }
        }
    }
}
