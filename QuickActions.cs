using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LumenLite;

/// <summary>One-click Windows actions for the quick-actions widget.</summary>
internal static class QuickActions
{
    public static void Lock() => LockWorkStation();

    /// <summary>Asks the screens to go to sleep; moving the mouse wakes them.</summary>
    public static void ScreensOff(IntPtr hwnd) => SendMessage(hwnd, WM_SYSCOMMAND, SC_MONITORPOWER, 2);

    /// <summary>Items and bytes in the Recycle Bin of every drive.</summary>
    public static (long Count, long Bytes) RecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64NumItems, info.i64Size) : (0, 0);
    }

    /// <summary>Empties the Recycle Bin after Windows' own confirmation dialog.</summary>
    public static void EmptyRecycleBin(IntPtr owner) => SHEmptyRecycleBin(owner, null, 0);

    /// <summary>Explorer records "View > Show desktop icons" as HideIcons; the window layout differs between builds.</summary>
    public static bool DesktopIconsVisible()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return !(key?.GetValue("HideIcons") is int hidden && hidden != 0);
    }

    /// <summary>The same command as right-click desktop > View > Show desktop icons.</summary>
    public static void ToggleDesktopIcons()
    {
        var view = DesktopView();
        if (view != IntPtr.Zero)
            SendMessage(view, WM_COMMAND, 0x7402, 0);
    }

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool LightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    /// <summary>Switches apps and Windows between light and dark, like Settings > Personalisation > Colours.</summary>
    public static void SetLightTheme(bool light)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        }
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 200, out _);
    }

    /// <summary>The window holding the desktop icons sits under Progman, or under a WorkerW once a wallpaper slideshow has run.</summary>
    private static IntPtr DesktopView()
    {
        var view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        var worker = IntPtr.Zero;
        while (view == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
        return view;
    }

    private const int WM_COMMAND = 0x0111;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int SC_MONITORPOWER = 0xF170;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr owner, string? rootPath, uint flags);

    /// <summary>Packed on 8 bytes on x64, as the Windows SDK declares it.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }
}
