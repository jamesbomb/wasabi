using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace Wasabi.Core.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, DataFlow Flow);

public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, DataFlow.Render))
            .ToList();
    }

    public static IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, DataFlow.Capture))
            .ToList();
    }

    public static IReadOnlyList<ProcessInfo> GetAudioProcesses()
    {
        var seen = new HashSet<int>();
        var list = new List<ProcessInfo>();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrWhiteSpace(proc.MainWindowTitle) && proc.SessionId == 0)
                    continue;
                if (!seen.Add(proc.Id)) continue;
                list.Add(new ProcessInfo(proc.Id, proc.ProcessName, proc.MainWindowTitle));
            }
            catch
            {
                // Access denied for some system processes
            }
            finally
            {
                proc.Dispose();
            }
        }

        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed record ProcessInfo(int Id, string Name, string WindowTitle)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(WindowTitle) ? $"{Name} ({Id})" : $"{Name} — {WindowTitle}";
}
