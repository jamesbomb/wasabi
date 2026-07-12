using NAudio.Wave;

namespace Wasabi.Core.Audio;

public static class AudioFormatHelper
{
    public static readonly WaveFormat StandardFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    public const int SampleRate = 44100;
    public const int Channels = 2;
    public const int FrameSize = Channels * sizeof(float);

    public static float[] BytesToFloat(ReadOnlySpan<byte> data, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var count = data.Length / sizeof(float);
            var result = new float[count];
            for (var i = 0; i < count; i++)
                result[i] = BitConverter.ToInt32(data.Slice(i * 4, 4)) is var bits
                    ? BitConverter.Int32BitsToSingle(bits)
                    : 0f;
            return result;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var count = data.Length / 2;
            var result = new float[count];
            for (var i = 0; i < count; i++)
            {
                var sample = BitConverter.ToInt16(data.Slice(i * 2, 2));
                result[i] = sample / 32768f;
            }
            return result;
        }

        return [];
    }

    public static byte[] FloatToBytes(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        for (var i = 0; i < samples.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), samples[i]);
        return bytes;
    }

    public static void MixAdd(Span<float> target, ReadOnlySpan<float> source, float gain)
    {
        var len = Math.Min(target.Length, source.Length);
        for (var i = 0; i < len; i++)
            target[i] += source[i] * gain;
    }

    public static void ApplyGain(Span<float> buffer, float gain)
    {
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] *= gain;
    }

    public static void SoftClip(Span<float> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var v = buffer[i];
            if (v > 1f) buffer[i] = 1f;
            else if (v < -1f) buffer[i] = -1f;
        }
    }
}
