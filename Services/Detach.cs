using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace LumenLite;

/// <summary>
/// LumenLite started from a terminal, an editor or an agent's shell lands in that tool's job object, and goes when
/// the tool closes or recycles the job: silently, with nothing in any log. On start it checks for that and, when the
/// job lets processes break away, starts itself again outside it and lets the first copy exit.
/// </summary>
internal static class Detach
{
    public const string Flag = "--detached";

    /// <summary>True when a copy outside the job was started and this one should exit.</summary>
    public static bool Relaunched(string[] args)
    {
        if (args.Contains(Flag, StringComparer.OrdinalIgnoreCase) || Environment.ProcessPath is not { } exe)
            return false;
        try
        {
            if (!IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out var inJob) || !inJob)
                return false;
            // A copy just started this way that still finds itself in a job stays: no endless relaunching.
            var marker = System.IO.Path.Combine(Store.Folder, "detach.txt");
            if (System.IO.File.Exists(marker) && DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(marker) < TimeSpan.FromSeconds(20))
                return false;
            System.IO.Directory.CreateDirectory(Store.Folder);
            System.IO.File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            // Explorer runs outside every job (a packaged app such as the Claude desktop app keeps all it starts in
            // its own, which breakaway does not leave), so the copy it starts lives on whatever closes the launcher.
            var explorer = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(explorer, $"\"{exe}\"") { UseShellExecute = false });
            Store.Log("started from inside a job (a terminal or an app's shell): handed over to Explorer");
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or EntryPointNotFoundException or DllNotFoundException or System.IO.IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

}
