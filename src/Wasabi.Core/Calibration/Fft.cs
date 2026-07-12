using System.Numerics;

namespace Wasabi.Core.Calibration;

internal static class Fft
{
    public static void Transform(Complex[] buffer, bool inverse)
    {
        var count = buffer.Length;
        if (count == 0 || (count & (count - 1)) != 0)
            throw new ArgumentException("FFT input size must be a power of two.", nameof(buffer));

        for (int i = 1, j = 0; i < count; i++)
        {
            var bit = count >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }

        for (var length = 2; length <= count; length <<= 1)
        {
            var angle = 2 * Math.PI / length * (inverse ? 1 : -1);
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var offset = 0; offset < count; offset += length)
            {
                var rotation = Complex.One;
                var half = length >> 1;
                for (var i = 0; i < half; i++)
                {
                    var even = buffer[offset + i];
                    var odd = buffer[offset + i + half] * rotation;
                    buffer[offset + i] = even + odd;
                    buffer[offset + i + half] = even - odd;
                    rotation *= step;
                }
            }
        }

        if (!inverse) return;
        for (var i = 0; i < count; i++)
            buffer[i] /= count;
    }

    public static int NextPowerOfTwo(int value)
    {
        var power = 1;
        while (power < value) power <<= 1;
        return power;
    }
}
