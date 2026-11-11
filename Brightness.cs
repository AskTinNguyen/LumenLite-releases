using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace LumenLite;

/// <summary>A monitor whose brightness can be changed; Level is 0..1.</summary>
public sealed record BrightnessMonitor(string Key, string Name, float Level);

/// <summary>
/// Monitor brightness over DDC/CI, the same channel as the monitor's own menu
/// buttons. Each call talks to the monitor over the cable and can take tens of
/// milliseconds, so everything runs on one background thread, and while a bar
/// is dragged only the latest value is sent.
/// </summary>
internal static class Brightness
{
    private sealed class Physical
    {
        public required IntPtr Handle;
        public required uint Min, Max;
    }

    private static readonly BlockingCollection<Action> Work = new();
    private static readonly ConcurrentDictionary<string, float> Pending = new();
    private static Dictionary<string, Physical> _monitors = new();

    static Brightness()
    {
        var thread = new Thread(() =>
        {
            foreach (var job in Work.GetConsumingEnumerable())
            {
                try
                {
                    job();
                }
                catch (Exception ex)
                {
                    Store.Log("brightness failed: " + ex.Message);
                }
            }
        })
        { IsBackground = true, Name = "LumenLite brightness" };
        thread.Start();
    }

    /// <summary>Lists monitors that answer over DDC/CI; the callback runs on the background thread.</summary>
    public static void Read(Action<List<BrightnessMonitor>> done) => Work.Add(() =>
    {
        foreach (var old in _monitors.Values)
            DestroyPhysicalMonitor(old.Handle);
        _monitors = new Dictionary<string, Physical>();

        var names = new Dictionary<string, string>();
        try
        {
            names = Displays.NamesByGdiDevice();
        }
        catch (Exception ex)
        {
            Store.Log("display names failed: " + ex.Message);
        }

        var result = new List<BrightnessMonitor>();
        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        }, IntPtr.Zero);

        foreach (var monitor in handles)
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            GetMonitorInfo(monitor, ref info);
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
                continue;
            var physical = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
                continue;
            for (var i = 0; i < physical.Length; i++)
            {
                var handle = physical[i].hPhysicalMonitor;
                if (!GetMonitorBrightness(handle, out var min, out var current, out var max) || max <= min)
                {
                    DestroyPhysicalMonitor(handle);
                    continue;
                }
                var key = info.szDevice + "#" + i;
                _monitors[key] = new Physical { Handle = handle, Min = min, Max = max };
                var name = names.TryGetValue(info.szDevice, out var friendly) ? friendly : physical[i].szPhysicalMonitorDescription;
                result.Add(new BrightnessMonitor(key, name, (float)(current - min) / (max - min)));
            }
        }
        done(result);
    });

    /// <summary>Queues a change; repeated calls while dragging collapse into the newest value.</summary>
    /// <summary>Raised on the caller's thread with the new level, for the Dynamic Island.</summary>
    public static event Action<float>? Changed;

    public static void Set(string key, float level)
    {
        Pending[key] = Math.Clamp(level, 0f, 1f);
        Changed?.Invoke(Math.Clamp(level, 0f, 1f));
        // Every call queues a job; the first one to run sends the newest value, the rest find nothing.
        Work.Add(() =>
        {
            if (!Pending.TryRemove(key, out var value) || !_monitors.TryGetValue(key, out var monitor))
                return;
            SetMonitorBrightness(monitor.Handle, monitor.Min + (uint)Math.Round(value * (monitor.Max - monitor.Min)));
        });
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public Native.RECT rcMonitor;
        public Native.RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
}
