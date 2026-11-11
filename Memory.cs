using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace LumenLite;

/// <summary>
/// Gives memory back to Windows when LumenLite goes quiet: after start-up and
/// after a window closes. Collects the .NET heap, then lets Windows page out what
/// is not in use, which is what Task Manager's "Memory" column shows.
/// </summary>
internal static class Memory
{
    private static DispatcherTimer? _soon;

    /// <summary>
    /// One trim once start-up has settled. There is no periodic trim any more: paging the
    /// process out every few minutes made the next mouse move or animation fault it back in.
    /// </summary>
    public static void Start() => TrimSoon(TimeSpan.FromSeconds(45));

    /// <summary>Trims a moment later, once closing animations and layout have finished.</summary>
    public static void TrimSoon(TimeSpan? delay = null)
    {
        if (_soon == null)
        {
            _soon = new DispatcherTimer();
            _soon.Tick += (_, _) =>
            {
                _soon.Stop();
                Trim();
            };
        }
        _soon.Stop();
        _soon.Interval = delay ?? TimeSpan.FromSeconds(2);
        _soon.Start();
    }

    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            using var process = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(process.Handle, -1, -1);
        }
        catch (Exception ex)
        {
            Store.Log("memory trim failed: " + ex.Message);
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    private static bool SetProcessWorkingSetSize(IntPtr process, int minimum, int maximum) =>
        SetProcessWorkingSetSize(process, new IntPtr(minimum), new IntPtr(maximum));
}
