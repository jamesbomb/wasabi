using System.Numerics;

namespace Wasabi.Core.Calibration;

public sealed record DelayEstimate(int DelaySamples, double DelayMilliseconds, double Confidence);

public static class DelayEstimator
{
    /// <summary>
    /// Finds a known probe sequence in a microphone recording using GCC-PHAT:
    /// FFT(recording) * conjugate(FFT(probe)), phase-normalized, then inverse FFT.
    /// The phase-only form is resilient to microphone gain and room reflections.
    /// </summary>
    public static DelayEstimate? Estimate(
        ReadOnlySpan<float> recording,
        ReadOnlySpan<float> probe,
        int sampleRate,
        int maximumDelayMilliseconds = 1500)
    {
        if (recording.Length < probe.Length || probe.Length < 32 || sampleRate <= 0)
            return null;

        var fftLength = Fft.NextPowerOfTwo(recording.Length + probe.Length - 1);
        var capturedSpectrum = new Complex[fftLength];
        var probeSpectrum = new Complex[fftLength];

        for (var i = 0; i < recording.Length; i++)
            capturedSpectrum[i] = new Complex(recording[i], 0);
        for (var i = 0; i < probe.Length; i++)
            probeSpectrum[i] = new Complex(probe[i], 0);

        Fft.Transform(capturedSpectrum, inverse: false);
        Fft.Transform(probeSpectrum, inverse: false);

        for (var i = 0; i < fftLength; i++)
        {
            var crossPower = capturedSpectrum[i] * Complex.Conjugate(probeSpectrum[i]);
            var magnitude = crossPower.Magnitude;
            capturedSpectrum[i] = magnitude > 1e-10 ? crossPower / magnitude : Complex.Zero;
        }

        Fft.Transform(capturedSpectrum, inverse: true);

        var maxDelaySamples = Math.Min(
            recording.Length - probe.Length,
            sampleRate * maximumDelayMilliseconds / 1000);
        var peakIndex = 0;
        var peak = 0d;
        var values = new List<double>(Math.Max(1, maxDelaySamples / 16));

        for (var i = 0; i <= maxDelaySamples; i++)
        {
            var magnitude = Math.Abs(capturedSpectrum[i].Real);
            if (magnitude > peak)
            {
                peak = magnitude;
                peakIndex = i;
            }

            if (i % 16 == 0)
                values.Add(magnitude);
        }

        if (peak <= 0 || values.Count == 0)
            return null;

        values.Sort();
        var medianNoise = values[values.Count / 2];
        var confidence = peak / Math.Max(medianNoise, 1e-8);
        return new DelayEstimate(peakIndex, peakIndex * 1000d / sampleRate, confidence);
    }
}
