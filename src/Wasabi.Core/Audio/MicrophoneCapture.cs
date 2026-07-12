using NAudio.CoreAudioApi;
using NAudio.Wave;
using Wasabi.Core.Diagnostics;

namespace Wasabi.Core.Audio;

/// <summary>
/// Collects a short microphone recording for acoustic calibration.
/// It is deliberately separate from the routing engine: calibration never
/// inserts the microphone signal into the user's audio graph.
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    private readonly WasapiCapture _capture;
    private readonly List<float> _samples = [];
    private readonly object _sync = new();

    public int SampleRate => _capture.WaveFormat.SampleRate;
    public bool IsRecording { get; private set; }

    public MicrophoneCapture(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        _capture = new WasapiCapture(device, true, 50);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, args) =>
        {
            IsRecording = false;
            if (args.Exception is not null)
                WasabiLog.Error("Errore nella cattura del microfono di calibrazione.", args.Exception);
        };
    }

    public void Start()
    {
        lock (_sync)
            _samples.Clear();

        _capture.StartRecording();
        IsRecording = true;
        WasabiLog.Info($"Cattura microfono di calibrazione avviata ({SampleRate} Hz).");
    }

    public float[] Stop()
    {
        if (IsRecording)
            _capture.StopRecording();

        lock (_sync)
            return _samples.ToArray();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var mono = AudioFormatHelper.BytesToMono(args.Buffer.AsSpan(0, args.BytesRecorded), _capture.WaveFormat);
        if (mono.Length == 0) return;

        lock (_sync)
            _samples.AddRange(mono);
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }
}
