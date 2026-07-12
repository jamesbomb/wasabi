using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Wasabi.Core.Calibration;

internal static class TestTonePlayer
{
    public static async Task PlayAsync(
        string deviceId,
        ReadOnlyMemory<float> monoProbe,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var output = new WasapiOut(
            enumerator.GetDevice(deviceId),
            AudioClientShareMode.Shared,
            false,
            50);

        const int preRollMilliseconds = 300;
        const int tailMilliseconds = 250;
        var preRollFrames = sampleRate * preRollMilliseconds / 1000;
        var tailFrames = sampleRate * tailMilliseconds / 1000;
        var frameCount = preRollFrames + monoProbe.Length + tailFrames;
        var stereo = new float[frameCount * 2];

        for (var i = 0; i < monoProbe.Length; i++)
        {
            var sample = monoProbe.Span[i];
            var index = (preRollFrames + i) * 2;
            stereo[index] = sample;
            stereo[index + 1] = sample;
        }

        var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };
        var bytes = new byte[stereo.Length * sizeof(float)];
        Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);

        output.Init(provider);
        provider.AddSamples(bytes, 0, bytes.Length);
        output.Play();
        await Task.Delay(preRollMilliseconds + (int)Math.Ceiling(monoProbe.Length * 1000d / sampleRate) + tailMilliseconds, cancellationToken);
        output.Stop();
    }

    public static float[] CreateLogChirp(int sampleRate)
    {
        const double durationSeconds = 0.12;
        const double startFrequency = 550;
        var endFrequency = Math.Min(6500d, sampleRate * 0.42);
        var sampleCount = (int)(sampleRate * durationSeconds);
        var chirp = new float[sampleCount];
        var ratio = endFrequency / startFrequency;
        var coefficient = 2 * Math.PI * startFrequency * durationSeconds / Math.Log(ratio);
        var fadeSamples = Math.Max(1, sampleRate / 200); // 5 ms

        for (var i = 0; i < sampleCount; i++)
        {
            var time = i / (double)sampleRate;
            var phase = coefficient * (Math.Pow(ratio, time / durationSeconds) - 1);
            var fade = Math.Min(1d, Math.Min(i / (double)fadeSamples, (sampleCount - 1 - i) / (double)fadeSamples));
            chirp[i] = (float)(0.35 * fade * Math.Sin(phase));
        }

        return chirp;
    }
}
