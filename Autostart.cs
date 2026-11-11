using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace LumenLite;

/// <summary>
/// Starts LumenLite at sign-in through a shortcut in the user's Startup folder.
/// An earlier version used the Run registry key; on this machine Windows
/// skipped that entry at sign-in, so it is moved to the shortcut on launch.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "LumenLite";

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "LumenLite.lnk");

    public static bool IsEnabled => File.Exists(ShortcutPath);

    public static void Set(bool enabled)
    {
        RemoveRunValue();
        if (enabled)
            CreateShortcut();
        else if (File.Exists(ShortcutPath))
            File.Delete(ShortcutPath);
    }

    /// <summary>Replaces an old Run entry, and keeps the shortcut pointing at this copy of the exe.</summary>
    public static void Migrate()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(RunValue) is string || IsEnabled)
                Set(true);
        }
        catch (Exception ex)
        {
            Store.Log("autostart migrate failed: " + ex.Message);
        }
    }

    private static void RemoveRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    private static void CreateShortcut()
    {
        var exe = LongPath(Environment.ProcessPath!);
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exe);
        link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
        link.SetDescription("LumenLite");
        ((IPersistFile)link).Save(ShortcutPath, true);
        Marshal.ReleaseComObject(link);
    }

    /// <summary>Expands 8.3 names such as TAILIU~1 back to the real folder names.</summary>
    private static string LongPath(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetLongPathName(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int size);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    /// <summary>Slots up to SetPath, in vtable order; unused ones are placeholders.</summary>
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr file, int size, IntPtr findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription(IntPtr name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(IntPtr dir, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(IntPtr args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(IntPtr path, int size, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
