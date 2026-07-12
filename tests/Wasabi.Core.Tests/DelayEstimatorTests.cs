using Wasabi.Core.Calibration;
using Xunit;

namespace Wasabi.Core.Tests;

public sealed class DelayEstimatorTests
{
    [Fact]
    public void Estimate_FindsKnownDelayInNoisyRecording()
    {
        const int sampleRate = 44_100;
        const int expectedDelay = 12_345;
        var probe = CreateProbe(2_048);
        var recording = new float[40_000];
        var random = new Random(17);

        for (var i = 0; i < recording.Length; i++)
            recording[i] = (float)(random.NextDouble() - 0.5) * 0.01f;
        for (var i = 0; i < probe.Length; i++)
            recording[expectedDelay + i] += probe[i] * 0.65f;

        var estimate = DelayEstimator.Estimate(recording, probe, sampleRate);

        Assert.NotNull(estimate);
        Assert.InRange(estimate!.DelaySamples, expectedDelay - 1, expectedDelay + 1);
        Assert.True(estimate.Confidence >= 8);
    }

    private static float[] CreateProbe(int sampleCount)
    {
        var probe = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var phase = 2 * Math.PI * (0.002 * i + 0.000001 * i * i);
            probe[i] = (float)Math.Sin(phase);
        }
        return probe;
    }
}
