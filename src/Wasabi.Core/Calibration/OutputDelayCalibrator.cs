using Wasabi.Core.Audio;
using Wasabi.Core.Diagnostics;
using Wasabi.Core.Routing;

namespace Wasabi.Core.Calibration;

public sealed record OutputCalibrationMeasurement(
    string NodeId,
    string OutputName,
    double? MeasuredLatencyMs,
    double Confidence,
    int ValidRuns,
    string? Warning);

public sealed record CalibrationResult(
    IReadOnlyList<OutputCalibrationMeasurement> Measurements,
    IReadOnlyDictionary<string, int> SuggestedDelayMsByNodeId);

public sealed class OutputDelayCalibrator
{
    private const int AttemptsPerOutput = 3;
    private const double MinimumConfidence = 8;
    private const int MaximumDelayMs = 500;

    public async Task<CalibrationResult> MeasureAsync(
        IReadOnlyList<RoutingNode> outputs,
        string microphoneDeviceId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var configuredOutputs = outputs
            .Where(n => n.Type == NodeType.DeviceOutput && !string.IsNullOrWhiteSpace(n.DeviceId))
            .ToList();
        if (configuredOutputs.Count < 2)
            throw new InvalidOperationException("Configura almeno due uscite audio prima di calibrare.");

        using var microphone = new MicrophoneCapture(microphoneDeviceId);
        var probe = TestTonePlayer.CreateLogChirp(microphone.SampleRate);
        var measurements = new List<OutputCalibrationMeasurement>();

        foreach (var output in configuredOutputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Misuro {output.Title}…");
            WasabiLog.Info($"Calibrazione: avvio misura uscita '{output.Title}'.");

            var attempts = new List<DelayEstimate>();
            for (var attempt = 1; attempt <= AttemptsPerOutput; attempt++)
            {
                progress?.Report($"Misuro {output.Title} ({attempt}/{AttemptsPerOutput})…");
                microphone.Start();
                try
                {
                    await Task.Delay(150, cancellationToken);
                    await TestTonePlayer.PlayAsync(output.DeviceId!, probe, microphone.SampleRate, cancellationToken);
                    await Task.Delay(120, cancellationToken);
                }
                finally
                {
                    var recording = microphone.Stop();
                    var estimate = DelayEstimator.Estimate(recording, probe, microphone.SampleRate);
                    if (estimate is not null && estimate.Confidence >= MinimumConfidence)
                    {
                        attempts.Add(estimate);
                        WasabiLog.Info(
                            $"Calibrazione '{output.Title}' tentativo {attempt}: {estimate.DelayMilliseconds:F1} ms, confidenza {estimate.Confidence:F1}.");
                    }
                    else
                    {
                        WasabiLog.Info(
                            $"Calibrazione '{output.Title}' tentativo {attempt}: segnale non affidabile " +
                            $"(confidenza {estimate?.Confidence:F1}).");
                    }
                }
            }

            if (attempts.Count == 0)
            {
                measurements.Add(new OutputCalibrationMeasurement(
                    output.Id,
                    output.Title,
                    null,
                    0,
                    0,
                    "Segnale non rilevato. Alza il volume o usa un microfono che senta questa uscita."));
                continue;
            }

            var ordered = attempts.OrderBy(a => a.DelayMilliseconds).ToList();
            var median = ordered[ordered.Count / 2];
            measurements.Add(new OutputCalibrationMeasurement(
                output.Id,
                output.Title,
                median.DelayMilliseconds,
                attempts.Average(a => a.Confidence),
                attempts.Count,
                attempts.Count == AttemptsPerOutput ? null : "Misura parziale: ripeti in un ambiente più silenzioso."));
        }

        var valid = measurements.Where(m => m.MeasuredLatencyMs is not null).ToList();
        var suggestions = new Dictionary<string, int>();
        if (valid.Count >= 2)
        {
            // The slowest path is the reference. Delay the faster endpoints,
            // never try to make an endpoint play earlier than its hardware allows.
            var slowestLatency = valid.Max(m => m.MeasuredLatencyMs!.Value);
            foreach (var measurement in valid)
            {
                var delay = Math.Clamp(
                    (int)Math.Round(slowestLatency - measurement.MeasuredLatencyMs!.Value),
                    0,
                    MaximumDelayMs);
                suggestions[measurement.NodeId] = delay;
            }
        }

        WasabiLog.Info($"Calibrazione terminata: {valid.Count}/{measurements.Count} uscite misurate.");
        return new CalibrationResult(measurements, suggestions);
    }
}
