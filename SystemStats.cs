using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LumenLite;

public sealed record SystemSample(
    double Cpu, double? Gpu,
    ulong MemoryUsed, ulong MemoryTotal,
    double DownloadPerSecond, double UploadPerSecond,
    string? LocalIp, long? DiskFree, long? DiskTotal,
    double? Battery = null, bool Charging = false);

/// <summary>
/// CPU and GPU load from the same performance counters Task Manager uses,
/// memory from GlobalMemoryStatusEx and traffic from the network adapters.
/// Not thread-safe: call Sample from one thread at a time.
/// </summary>
internal sealed class SystemStats : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x200;
    private const uint PDH_FMT_NOCAP100 = 0x8000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    private readonly IntPtr _query;
    private readonly IntPtr _cpu;
    private readonly IntPtr _gpu;
    private long _lastReceived = -1, _lastSent = -1;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTime;
    private string? _ip;
    private DateTime _ipRead = DateTime.MinValue;

    public SystemStats()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
            return;
        PdhAddEnglishCounter(_query, @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _cpu);
        if (_cpu == IntPtr.Zero)
            PdhAddEnglishCounter(_query, @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _cpu);
        PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu);
        PdhCollectQueryData(_query);
    }

    /// <param name="gpu">Reading the per-process GPU engines is the expensive part; skip it when nobody shows it.</param>
    public SystemSample Sample(bool gpu = true)
    {
        double cpu = 0;
        double? gpuValue = null;
        var includeGpu = gpu;
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            if (_cpu != IntPtr.Zero && PdhGetFormattedCounterValue(_cpu, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, IntPtr.Zero, out var value) == 0 && value.CStatus == 0)
                cpu = Math.Clamp(value.doubleValue, 0, 100);
            if (_gpu != IntPtr.Zero && includeGpu)
                gpuValue = SumArray(_gpu);
        }

        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref memory);

        var (down, up) = Traffic();

        if (DateTime.UtcNow - _ipRead > TimeSpan.FromSeconds(30))
        {
            _ipRead = DateTime.UtcNow;
            _ip = LocalIp();
        }

        long? free = null, total = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            free = drive.AvailableFreeSpace;
            total = drive.TotalSize;
        }
        catch
        {
        }

        // Desktops report "no system battery" and get no battery row.
        var power = System.Windows.Forms.SystemInformation.PowerStatus;
        double? battery = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            || power.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.Unknown ? null : power.BatteryLifePercent;
        var charging = power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;

        return new SystemSample(cpu, gpuValue, memory.ullTotalPhys - memory.ullAvailPhys, memory.ullTotalPhys, down, up, _ip, free, total, battery, charging);
    }

    /// <summary>Adds up the 3D engines of every process, like Task Manager's GPU column.</summary>
    private static double? SumArray(IntPtr counter)
    {
        uint size = 0;
        var status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0)
            return status == 0 ? 0 : null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, buffer) != 0)
                return null;
            double sum = 0;
            var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (item.value.CStatus == 0)
                    sum += item.value.doubleValue;
            }
            return Math.Clamp(sum, 0, 100);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private (double Down, double Up) Traffic()
    {
        long received = 0, sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            try
            {
                var stats = adapter.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
            }
        }
        var now = _clock.Elapsed;
        var seconds = (now - _lastTime).TotalSeconds;
        double down = 0, up = 0;
        if (_lastReceived >= 0 && seconds > 0)
        {
            down = Math.Max(0, received - _lastReceived) / seconds;
            up = Math.Max(0, sent - _lastSent) / seconds;
        }
        _lastReceived = received;
        _lastSent = sent;
        _lastTime = now;
        return (down, up);
    }

    /// <summary>The IPv4 address of the adapter that has a default gateway.</summary>
    private static string? LocalIp()
    {
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up)
                    continue;
                var props = adapter.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
                    continue;
                var address = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (address != null)
                    return address.Address.ToString();
            }
        }
        catch
        {
        }
        return null;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
            PdhCloseQuery(_query);
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
