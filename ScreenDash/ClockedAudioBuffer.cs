namespace ScreenDash;

/// <summary>Fixed-capacity PCM timeline; old frames are overwritten and missing positions read as silence.</summary>
internal sealed class ClockedAudioBuffer
{
    private readonly byte[] _ring;
    private readonly long[] _positions;
    private readonly int _align;
    private readonly object _sync = new();
    public int CapacityFrames => _positions.Length;

    public ClockedAudioBuffer(int capacityFrames, int blockAlign)
    {
        if (capacityFrames <= 0 || blockAlign <= 0) throw new ArgumentOutOfRangeException(nameof(capacityFrames));
        _align = blockAlign;
        _ring = new byte[checked(capacityFrames * blockAlign)];
        _positions = new long[capacityFrames];
        Array.Fill(_positions, -1L);
    }

    public void Write(long startFrame, byte[] input, int frames)
    {
        if (startFrame < 0 || frames < 0 || (long)frames * _align > input.Length || startFrame > long.MaxValue - frames)
            throw new ArgumentOutOfRangeException(nameof(frames));
        lock (_sync)
        {
            // An oversized callback retains only its newest frames, without allocating a larger ring.
            var skip = Math.Max(0, frames - CapacityFrames);
            var firstSlot = (int)((startFrame + skip) % CapacityFrames);
            var retainedFrames = frames - skip;
            var firstSpan = Math.Min(retainedFrames, CapacityFrames - firstSlot);
            Buffer.BlockCopy(input, skip * _align, _ring, firstSlot * _align, firstSpan * _align);
            if (firstSpan < retainedFrames)
                Buffer.BlockCopy(input, (skip + firstSpan) * _align, _ring, 0, (retainedFrames - firstSpan) * _align);
            for (var i = skip; i < frames; i++)
            {
                var position = startFrame + i;
                _positions[(int)(position % CapacityFrames)] = position;
            }
        }
    }

    public void Read(long startFrame, byte[] output, int frames)
    {
        if (startFrame < 0 || frames < 0 || frames > CapacityFrames || (long)frames * _align > output.Length || startFrame > long.MaxValue - frames)
            throw new ArgumentOutOfRangeException(nameof(frames));
        lock (_sync)
        {
            var firstSlot = (int)(startFrame % CapacityFrames);
            var firstSpan = Math.Min(frames, CapacityFrames - firstSlot);
            Buffer.BlockCopy(_ring, firstSlot * _align, output, 0, firstSpan * _align);
            if (firstSpan < frames)
                Buffer.BlockCopy(_ring, 0, output, firstSpan * _align, (frames - firstSpan) * _align);
            var i = 0;
            while (i < frames)
            {
                var position = startFrame + i;
                if (_positions[(int)(position % CapacityFrames)] == position) { i++; continue; }
                var missingStart = i++;
                while (i < frames && _positions[(int)((startFrame + i) % CapacityFrames)] != startFrame + i) i++;
                Array.Clear(output, missingStart * _align, (i - missingStart) * _align);
            }
        }
    }
}
