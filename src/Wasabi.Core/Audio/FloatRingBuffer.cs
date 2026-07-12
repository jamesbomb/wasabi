namespace Wasabi.Core.Audio;

public sealed class FloatRingBuffer
{
    private readonly float[] _buffer;
    private int _write;
    private int _read;
    private int _count;
    private readonly object _lock = new();

    public FloatRingBuffer(int capacitySamples)
    {
        _buffer = new float[capacitySamples];
    }

    public int Available => _count;

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (var sample in samples)
            {
                if (_count == _buffer.Length)
                {
                    _read = (_read + 1) % _buffer.Length;
                    _count--;
                }

                _buffer[_write] = sample;
                _write = (_write + 1) % _buffer.Length;
                _count++;
            }
        }
    }

    public int Read(Span<float> destination)
    {
        lock (_lock)
        {
            var toRead = Math.Min(destination.Length, _count);
            for (var i = 0; i < toRead; i++)
            {
                destination[i] = _buffer[_read];
                _read = (_read + 1) % _buffer.Length;
            }
            _count -= toRead;
            return toRead;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _write = 0;
            _read = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }
}
