using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using Wasabi.Core.Audio;
using Wasabi.Core.Diagnostics;
using Wasabi.Core.Routing;

namespace Wasabi.Core.Engine;

public sealed class RoutingEngine : IDisposable
{
    private readonly RoutingGraph _graph;
    private readonly Dictionary<string, FloatRingBuffer> _portBuffers = new();
    private readonly Dictionary<string, IWavePlayer> _outputs = new();
    private readonly Dictionary<string, BufferedWaveProvider> _waveProviders = new();
    private readonly List<IDisposable> _captures = [];
    private Thread? _mixThread;
    private volatile bool _running;
    private readonly object _sync = new();

    private const int RingCapacity = AudioFormatHelper.SampleRate; // ~0.5 sec stereo
    private const int MixFrameCount = AudioFormatHelper.SampleRate / 100; // 10 ms
    private const int MixSampleCount = MixFrameCount * AudioFormatHelper.Channels;

    public bool IsRunning => _running;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public RoutingEngine(RoutingGraph graph) => _graph = graph;

    public void Start()
    {
        lock (_sync)
        {
            if (_running) return;
            WasabiLog.Info($"Avvio routing: {_graph.Nodes.Count} blocchi, {_graph.Connections.Count} collegamenti.");
            StopInternal();

            foreach (var node in _graph.Nodes)
            {
                foreach (var port in node.Ports)
                    _portBuffers[port.Id] = new FloatRingBuffer(RingCapacity);
            }

            StartSources();
            StartOutputs();
            _running = true;
            _mixThread = new Thread(MixLoop) { IsBackground = true, Name = "WasabiMix", Priority = ThreadPriority.AboveNormal };
            _mixThread.Start();
            WasabiLog.Info("Motore di mix avviato.");
            StatusChanged?.Invoke(this, "Routing attivo");
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopInternal();
            WasabiLog.Info("Routing fermato.");
            StatusChanged?.Invoke(this, "Routing fermato");
        }
    }

    private void StopInternal()
    {
        _running = false;
        _mixThread?.Join(1500);
        _mixThread = null;

        foreach (var capture in _captures)
        {
            try { capture.Dispose(); } catch { /* ignore */ }
        }
        _captures.Clear();

        foreach (var output in _outputs.Values)
        {
            try
            {
                output.Stop();
                output.Dispose();
            }
            catch { /* ignore */ }
        }
        _outputs.Clear();
        _waveProviders.Clear();
        _portBuffers.Clear();
    }

    private void StartSources()
    {
        foreach (var node in _graph.Nodes)
        {
            switch (node.Type)
            {
                case NodeType.AppSource when node.ProcessId is int pid:
                    StartAppSource(node, pid);
                    break;
                case NodeType.DeviceLoopback when !string.IsNullOrEmpty(node.DeviceId):
                    StartDeviceLoopback(node, node.DeviceId);
                    break;
            }
        }
    }

    private void StartAppSource(RoutingNode node, int processId)
    {
        try
        {
            WasabiLog.Info($"Apertura sorgente app '{node.Title}' (PID {processId}).");
            var capture = ProcessLoopbackCapture.CreateAsync(processId).GetAwaiter().GetResult();
            var outPort = node.Ports.First(p => p.Direction == PortDirection.Output);
            capture.DataAvailable += (_, e) =>
            {
                if (node.Muted) return;
                var floats = AudioFormatHelper.BytesToFloat(e.Buffer.AsSpan(0, e.BytesRecorded), capture.WaveFormat);
                AudioFormatHelper.ApplyGain(floats, node.Volume);
                _portBuffers[outPort.Id].Write(floats);
            };
            capture.StartRecording();
            _captures.Add(capture);
            WasabiLog.Info($"Sorgente app '{node.Title}' attiva.");
        }
        catch (Exception ex)
        {
            WasabiLog.Error($"Impossibile catturare '{node.Title}'.", ex);
            ErrorOccurred?.Invoke(this, new InvalidOperationException($"Impossibile catturare {node.Title}: {ex.Message}", ex));
        }
    }

    private void StartDeviceLoopback(RoutingNode node, string deviceId)
    {
        try
        {
            WasabiLog.Info($"Apertura loopback '{node.Title}' ({deviceId}).");
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);
            var capture = new WasapiLoopbackCapture(device);
            var outPort = node.Ports.First(p => p.Direction == PortDirection.Output);
            capture.DataAvailable += (_, e) =>
            {
                if (node.Muted) return;
                var floats = AudioFormatHelper.BytesToFloat(e.Buffer.AsSpan(0, e.BytesRecorded), capture.WaveFormat);
                AudioFormatHelper.ApplyGain(floats, node.Volume);
                _portBuffers[outPort.Id].Write(floats);
            };
            capture.StartRecording();
            _captures.Add(capture);
            WasabiLog.Info($"Loopback '{node.Title}' attivo.");
        }
        catch (Exception ex)
        {
            WasabiLog.Error($"Impossibile catturare dal dispositivo '{node.Title}'.", ex);
            ErrorOccurred?.Invoke(this, new InvalidOperationException($"Impossibile catturare da {node.Title}: {ex.Message}", ex));
        }
    }

    private void StartOutputs()
    {
        foreach (var node in _graph.Nodes.Where(n => n.Type == NodeType.DeviceOutput))
        {
            if (string.IsNullOrEmpty(node.DeviceId)) continue;

            try
            {
                WasabiLog.Info($"Apertura uscita '{node.Title}' ({node.DeviceId}).");
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(node.DeviceId);
                var provider = new BufferedWaveProvider(AudioFormatHelper.StandardFormat)
                {
                    // Large buffers turn small clock differences between endpoints into
                    // seconds of delay. Keep a bounded low-latency queue instead.
                    BufferDuration = TimeSpan.FromMilliseconds(250),
                    DiscardOnBufferOverflow = true
                };
                var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
                output.Init(provider);
                output.Play();
                _waveProviders[node.Id] = provider;
                _outputs[node.Id] = output;
                WasabiLog.Info($"Uscita '{node.Title}' attiva.");
            }
            catch (Exception ex)
            {
                WasabiLog.Error($"Impossibile aprire uscita '{node.Title}'.", ex);
                ErrorOccurred?.Invoke(this, new InvalidOperationException($"Impossibile aprire uscita {node.Title}: {ex.Message}", ex));
            }
        }
    }

    private void MixLoop()
    {
        var scratch = new float[MixSampleCount];
        var nodeInputs = new Dictionary<string, float[]>();
        var clock = Stopwatch.StartNew();
        var nextTick = TimeSpan.Zero;
        var blockDuration = TimeSpan.FromSeconds((double)MixFrameCount / AudioFormatHelper.SampleRate);

        while (_running)
        {
            try
            {
                foreach (var node in _graph.Nodes)
                {
                    if (node.Type is NodeType.Mixer or NodeType.Splitter or NodeType.VirtualBus or NodeType.DeviceOutput)
                        nodeInputs[node.Id] = new float[scratch.Length];
                }

                // Route along connections into node input accumulators
                foreach (var conn in _graph.Connections)
                {
                    if (!_portBuffers.TryGetValue(conn.SourcePortId, out var sourceBuffer)) continue;
                    var targetPort = _graph.FindPort(conn.TargetPortId);
                    if (targetPort is null) continue;
                    if (!nodeInputs.TryGetValue(targetPort.NodeId, out var acc)) continue;

                    var temp = new float[scratch.Length];
                    var read = sourceBuffer.Read(temp);
                    if (read == 0) continue;
                    AudioFormatHelper.MixAdd(acc.AsSpan(0, read), temp.AsSpan(0, read), 1f);
                }

                // Process nodes
                foreach (var node in _graph.Nodes)
                {
                    switch (node.Type)
                    {
                        case NodeType.Mixer:
                            ProcessMixer(node, nodeInputs, scratch);
                            break;
                        case NodeType.Splitter:
                            ProcessSplitter(node, nodeInputs);
                            break;
                        case NodeType.VirtualBus:
                            ProcessVirtualBus(node, nodeInputs);
                            break;
                        case NodeType.DeviceOutput:
                            ProcessDeviceOutput(node, nodeInputs);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                WasabiLog.Error("Errore nel ciclo di mix.", ex);
                ErrorOccurred?.Invoke(this, ex);
            }

            // Feed exactly the number of samples represented by each audio block.
            // The previous fixed 5 ms sleep sent 16% too many samples at 44.1 kHz,
            // filling endpoint queues and causing delay/distortion.
            nextTick += blockDuration;
            var remaining = nextTick - clock.Elapsed;
            if (remaining > TimeSpan.FromMilliseconds(1))
                Thread.Sleep((int)remaining.TotalMilliseconds);
            else if (remaining < -blockDuration)
                nextTick = clock.Elapsed;
        }
    }

    private void ProcessMixer(RoutingNode node, Dictionary<string, float[]> nodeInputs, float[] scratch)
    {
        if (!nodeInputs.TryGetValue(node.Id, out var mixed)) return;
        AudioFormatHelper.SoftClip(mixed);
        if (node.Muted) return;

        AudioFormatHelper.ApplyGain(mixed, node.Volume);
        var outPort = node.Ports.First(p => p.Direction == PortDirection.Output);
        _portBuffers[outPort.Id].Write(mixed);
    }

    private void ProcessSplitter(RoutingNode node, Dictionary<string, float[]> nodeInputs)
    {
        if (!nodeInputs.TryGetValue(node.Id, out var input)) return;
        if (node.Muted) return;

        AudioFormatHelper.ApplyGain(input, node.Volume);
        foreach (var port in node.Ports.Where(p => p.Direction == PortDirection.Output))
            _portBuffers[port.Id].Write(input);
    }

    private void ProcessVirtualBus(RoutingNode node, Dictionary<string, float[]> nodeInputs)
    {
        if (!nodeInputs.TryGetValue(node.Id, out var input)) return;
        AudioFormatHelper.SoftClip(input);
        if (node.Muted) return;

        AudioFormatHelper.ApplyGain(input, node.Volume);
        var outPort = node.Ports.First(p => p.Direction == PortDirection.Output);
        _portBuffers[outPort.Id].Write(input);
    }

    private void ProcessDeviceOutput(RoutingNode node, Dictionary<string, float[]> nodeInputs)
    {
        if (!_waveProviders.TryGetValue(node.Id, out var provider)) return;
        if (!nodeInputs.TryGetValue(node.Id, out var input)) return;
        if (node.Muted) return;

        AudioFormatHelper.SoftClip(input);
        AudioFormatHelper.ApplyGain(input, node.Volume);
        var bytes = AudioFormatHelper.FloatToBytes(input);
        provider.AddSamples(bytes, 0, bytes.Length);
    }

    public void Dispose() => Stop();
}
