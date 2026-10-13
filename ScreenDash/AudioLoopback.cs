using System.Diagnostics;
using System.IO.Pipes;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenDash;

/// <summary>Playback-device loopback. A bounded, clocked ring also emits silence when WASAPI emits no packets.</summary>
public sealed class AudioLoopback : IDisposable
{
    private readonly MMDevice _device;
    private readonly WasapiLoopbackCapture _capture;
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _ringLock = new();
    private readonly byte[] _ring;
    private readonly long[] _positions;
    private readonly Stopwatch _clock = new();
    private readonly int _capacityFrames;
    private long _lastCaptureEnd;
    private Task? _pump;
    private int _stopping;
    private int _faulted;
    private int _disposed;

    public event Action<Exception>? Faulted;
    public int SampleRate => _capture.WaveFormat.SampleRate;
    public int Channels => _capture.WaveFormat.Channels;
    public string RawFormat { get; }
    public string PipePath { get; }

    public static IReadOnlyList<AudioSource> GetDevices()
    {
        var result = new List<AudioSource> { new("", "Windows-Standardausgabe") };
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device) result.Add(new AudioSource(device.ID, device.FriendlyName));
        }
        return result;
    }

    public AudioLoopback(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        _device = string.IsNullOrEmpty(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        if (_device.DataFlow != DataFlow.Render || _device.State != DeviceState.Active)
        {
            _device.Dispose();
            throw new InvalidOperationException("Das ausgewählte Wiedergabegerät ist nicht verfügbar.");
        }
        try
        {
            _capture = new WasapiLoopbackCapture(_device);
            RawFormat = GetRawFormat(_capture.WaveFormat);
            if (SampleRate < 8000 || SampleRate > 192000 || Channels is < 1 or > 16)
                throw new InvalidOperationException("Das Audioformat des Ausgabegerätes wird nicht unterstützt.");
            _capacityFrames = checked(SampleRate * 2);
            _ring = new byte[checked(_capacityFrames * _capture.WaveFormat.BlockAlign)];
            _positions = new long[_capacityFrames];
            Array.Fill(_positions, -1L);
            var name = "ScreenDash-audio-" + Guid.NewGuid().ToString("N");
            PipePath = @"\\.\pipe\" + name;
            _pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
        }
        catch
        {
            _capture?.Dispose();
            _device.Dispose();
            throw;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_pump is not null) throw new InvalidOperationException("Audioaufnahme läuft bereits.");
        _clock.Start();
        _capture.StartRecording();
        _pump = Task.Run(PumpAsync);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_stopping != 0 || e.BytesRecorded == 0) return;
        var align = _capture.WaveFormat.BlockAlign;
        var frames = e.BytesRecorded / align;
        var end = (long)(_clock.Elapsed.TotalSeconds * SampleRate);
        var start = Math.Max(0, end - frames);
        lock (_ringLock)
        {
            // Smooth callback scheduling jitter, but re-anchor after a genuine silent gap.
            if (_lastCaptureEnd > 0 && Math.Abs(start - _lastCaptureEnd) <= SampleRate / 20)
                start = _lastCaptureEnd;
            var skip = Math.Max(0, frames - _capacityFrames);
            var firstSlot = (int)((start + skip) % _capacityFrames);
            var retainedFrames = frames - skip;
            var firstSpan = Math.Min(retainedFrames, _capacityFrames - firstSlot);
            Buffer.BlockCopy(e.Buffer, skip * align, _ring, firstSlot * align, firstSpan * align);
            if (firstSpan < retainedFrames)
                Buffer.BlockCopy(e.Buffer, (skip + firstSpan) * align, _ring, 0, (retainedFrames - firstSpan) * align);
            for (var i = skip; i < frames; i++)
            {
                var position = start + i;
                var slot = (int)(position % _capacityFrames);
                _positions[slot] = position;
            }
            _lastCaptureEnd = start + frames;
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping == 0)
            Fail(e.Exception ?? new IOException("Die Audioaufnahme wurde vom Ausgabegerät beendet."));
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
                        throw new IOException("Das ausgewählte Audioausgabegerät wurde getrennt oder deaktiviert.");
                }
                // 120 ms lets callback batches arrive before filling a missing time span with silence.
                var due = Math.Max(0, (long)((_clock.Elapsed.TotalSeconds - 0.12) * SampleRate));
                while (writtenFrames < due)
                {
                    ct.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(chunkFrames, due - writtenFrames);
                    lock (_ringLock)
                    {
                        var firstSlot = (int)(writtenFrames % _capacityFrames);
                        var firstSpan = Math.Min(count, _capacityFrames - firstSlot);
                        Buffer.BlockCopy(_ring, firstSlot * align, output, 0, firstSpan * align);
                        if (firstSpan < count)
                            Buffer.BlockCopy(_ring, 0, output, firstSpan * align, (count - firstSpan) * align);
                        var i = 0;
                        while (i < count)
                        {
                            var position = writtenFrames + i;
                            var slot = (int)(position % _capacityFrames);
                            if (_positions[slot] == position) { i++; continue; }
                            var missingStart = i++;
                            while (i < count && _positions[(int)((writtenFrames + i) % _capacityFrames)] != writtenFrames + i) i++;
                            Array.Clear(output, missingStart * align, (i - missingStart) * align);
                        }
                    }
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopAsync().GetAwaiter().GetResult();
        _capture.DataAvailable -= OnData;
        _capture.RecordingStopped -= OnStopped;
        _capture.Dispose();
        _device.Dispose();
        _shutdown.Dispose();
    }
}
