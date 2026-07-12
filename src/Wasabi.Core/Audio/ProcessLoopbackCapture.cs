using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;
using Wasabi.Core.Diagnostics;

namespace Wasabi.Core.Audio;

public sealed class ProcessLoopbackCapture : IDisposable
{
    private const string VirtualDevice = "VAD\\Process_Loopback";
    private AudioClient? _audioClient;
    private Thread? _captureThread;
    private volatile CaptureState _state = CaptureState.Stopped;
    private byte[] _recordBuffer = [];
    private int _bytesPerFrame;
    private WaveFormat _waveFormat = AudioFormatHelper.StandardFormat;
    private EventWaitHandle? _frameEvent;
    private int _bufferFrameCount = 2000;

    public WaveFormat WaveFormat => _waveFormat;
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public static async Task<ProcessLoopbackCapture> CreateAsync(int processId, bool includeProcessTree = true)
    {
        var capture = new ProcessLoopbackCapture();
        await capture.ActivateAsync(processId, includeProcessTree);
        return capture;
    }

    private async Task ActivateAsync(int processId, bool includeProcessTree)
    {
        WasabiLog.Info($"Attivazione Process Loopback: PID {processId}.");
        var activationParams = new AudioClientActivationParamsNative
        {
            ActivationType = AudioClientActivationTypeNative.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParamsNative
            {
                ProcessLoopbackMode = includeProcessTree
                    ? ProcessLoopbackModeNative.IncludeTargetProcessTree
                    : ProcessLoopbackModeNative.ExcludeTargetProcessTree,
                TargetProcessId = (uint)processId
            }
        };

        var blobPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParamsNative>());
        try
        {
            Marshal.StructureToPtr(activationParams, blobPtr, false);
            var propVariant = new PropVariantNative
            {
                vt = (short)VarEnumNative.VT_BLOB,
                blobVal = new BlobNative
                {
                    Length = Marshal.SizeOf<AudioClientActivationParamsNative>(),
                    Data = blobPtr
                }
            };

            var handler = new ActivateAudioInterfaceCompletionHandler(ac =>
            {
                _audioClient = new AudioClient(ac);
            });

            var iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            var propPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantNative>());
            try
            {
                Marshal.StructureToPtr(propVariant, propPtr, false);
                var activationResult = ProcessLoopbackNativeMethods.ActivateAudioInterfaceAsync(
                    VirtualDevice, ref iid, propPtr, handler, out _);
                if (activationResult < 0)
                    Marshal.ThrowExceptionForHR(activationResult);

                var completed = await Task.WhenAny(handler.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
                if (completed != handler.Task)
                    throw new TimeoutException($"La cattura dell'app (PID {processId}) non ha risposto entro 10 secondi.");

                await handler.Task.ConfigureAwait(false);
            }
            finally
            {
                Marshal.FreeHGlobal(propPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }

        if (_audioClient is null)
            throw new InvalidOperationException("Impossibile attivare la cattura loopback del processo.");

        WasabiLog.Info($"Process Loopback attivato: PID {processId}.");
    }

    public void StartRecording()
    {
        if (_audioClient is null) throw new InvalidOperationException("AudioClient non inizializzato.");
        if (_state != CaptureState.Stopped) throw new InvalidOperationException("Cattura già attiva.");

        _state = CaptureState.Starting;
        const long reftimesPerMillisec = 10000;
        const int bufferMs = 100;
        var streamFlags = AudioClientStreamFlags.Loopback
                          | AudioClientStreamFlags.EventCallback
                          | AudioClientStreamFlags.AutoConvertPcm
                          | AudioClientStreamFlags.SrcDefaultQuality;

        _audioClient.Initialize(
            AudioClientShareMode.Shared,
            streamFlags,
            reftimesPerMillisec * bufferMs,
            0,
            _waveFormat,
            Guid.Empty);

        _frameEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        _audioClient.SetEventHandle(_frameEvent.SafeWaitHandle.DangerousGetHandle());

        _bytesPerFrame = _waveFormat.Channels * _waveFormat.BitsPerSample / 8;
        _recordBuffer = new byte[_bufferFrameCount * _bytesPerFrame];

        _captureThread = new Thread(CaptureThread) { IsBackground = true, Name = "ProcessLoopback" };
        _captureThread.Start();
        WasabiLog.Info("Cattura Process Loopback avviata.");
    }

    public void StopRecording()
    {
        if (_state != CaptureState.Stopped)
            _state = CaptureState.Stopping;
    }

    private void CaptureThread()
    {
        Exception? error = null;
        try
        {
            var client = _audioClient!;
            var capture = client.AudioCaptureClient;
            client.Start();
            if (_state == CaptureState.Starting)
                _state = CaptureState.Capturing;

            while (_state == CaptureState.Capturing)
            {
                _frameEvent!.WaitOne(2000, false);
                if (_state != CaptureState.Capturing) break;
                ReadPackets(capture);
            }
        }
        catch (Exception ex)
        {
            error = ex;
            WasabiLog.Error("Errore nel thread di cattura Process Loopback.", ex);
        }
        finally
        {
            try { _audioClient?.Stop(); } catch { /* ignore */ }
            _state = CaptureState.Stopped;
            RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
        }
    }

    private void ReadPackets(AudioCaptureClient capture)
    {
        var offset = 0;
        while (capture.GetNextPacketSize() is var packetSize && packetSize > 0)
        {
            var buffer = capture.GetBuffer(out var frames, out var flags);
            var bytes = frames * _bytesPerFrame;

            if (offset + bytes > _recordBuffer.Length)
            {
                if (offset > 0)
                {
                    DataAvailable?.Invoke(this, new WaveInEventArgs(_recordBuffer, offset));
                    offset = 0;
                }
                if (bytes > _recordBuffer.Length)
                    _recordBuffer = new byte[bytes];
            }

            if ((flags & AudioClientBufferFlags.Silent) == AudioClientBufferFlags.Silent)
                Array.Clear(_recordBuffer, offset, bytes);
            else
                Marshal.Copy(buffer, _recordBuffer, offset, bytes);

            offset += bytes;
            capture.ReleaseBuffer(frames);
        }

        if (offset > 0)
            DataAvailable?.Invoke(this, new WaveInEventArgs(_recordBuffer, offset));
    }

    public void Dispose()
    {
        StopRecording();
        _captureThread?.Join(2000);
        _frameEvent?.Dispose();
        _audioClient?.Dispose();
        WasabiLog.Info("Cattura Process Loopback rilasciata.");
    }

    private enum CaptureState { Stopped, Starting, Capturing, Stopping }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParamsNative
    {
        public AudioClientActivationTypeNative ActivationType;
        public AudioClientProcessLoopbackParamsNative ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParamsNative
    {
        public uint TargetProcessId;
        public ProcessLoopbackModeNative ProcessLoopbackMode;
    }

    private enum AudioClientActivationTypeNative
    {
        Default,
        ProcessLoopback
    }

    private enum ProcessLoopbackModeNative
    {
        IncludeTargetProcessTree,
        ExcludeTargetProcessTree
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantNative
    {
        public short vt;
        public short wReserved1;
        public short wReserved2;
        public short wReserved3;
        public BlobNative blobVal;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlobNative
    {
        public int Length;
        public IntPtr Data;
    }

    private enum VarEnumNative : short
    {
        VT_BLOB = 0x0041
    }

    private static class ProcessLoopbackNativeMethods
    {
        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
        public static extern int ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            ref Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IntPtr activationOperation);
    }

    private sealed class ActivateAudioInterfaceCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly Action<IAudioClient> _onActivated;
        private readonly TaskCompletionSource<IAudioClient> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IAudioClient> Task => _tcs.Task;

        public ActivateAudioInterfaceCompletionHandler(Action<IAudioClient> onActivated) =>
            _onActivated = onActivated;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            operation.GetActivateResult(out var hr, out var punk);
            if (hr >= 0 && punk is IAudioClient client)
            {
                _onActivated(client);
                _tcs.TrySetResult(client);
            }
            else
            {
                _tcs.TrySetException(new COMException("ActivateAudioInterfaceAsync failed", hr));
            }
        }
    }
}
