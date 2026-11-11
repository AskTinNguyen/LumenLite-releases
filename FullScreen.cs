using System;
using System.Runtime.InteropServices;
using System.Text;
using Screen = System.Windows.Forms.Screen;

namespace LumenLite;

/// <summary>
/// Tells whether the window in front is a full-screen game or video. Shell
/// surfaces that also cover the screen (Start, Search, Alt+Tab, Task View, the
/// desktop) and ordinary maximised windows do not count, and neither do
/// LumenLite's own windows.
/// </summary>
internal static class FullScreen
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;

    private static readonly string[] ShellClasses =
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame", "TaskSwitcherWnd",
        "ApplicationManager_DesktopShellWindow", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "LockScreenControllerProxyWindow", "Windows.Internal.Shell.TabProxyWindow",
    };

    private static readonly int OwnProcess = Environment.ProcessId;

    /// <summary>True when the foreground app fills <paramref name="screen"/> (any screen when null).</summary>
    public static bool Foreground(Screen? screen = null)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == OwnProcess)
            return false;
        var name = new StringBuilder(96);
        GetClassName(hwnd, name, name.Capacity);
        if (Array.IndexOf(ShellClasses, name.ToString()) >= 0)
            return false;
        // A maximised window with a title bar is ordinary work, not a game or a video.
        if (IsZoomed(hwnd) || (GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & WS_CAPTION) == WS_CAPTION)
            return false;
        if (!Native.GetWindowRect(hwnd, out var rect))
            return false;
        var monitor = Screen.FromHandle(hwnd);
        if (screen != null && monitor.DeviceName != screen.DeviceName)
            return false;
        var b = monitor.Bounds;
        return rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
