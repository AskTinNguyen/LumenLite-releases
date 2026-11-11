using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

namespace LumenLite;

/// <summary>
/// Reads and toggles UniKey's Vietnamese/English mode through its tray icon,
/// the same click a user would make. UniKey has no other interface; its tray
/// tooltip ("Click to turn off Vietnamese mode") tells the current mode.
/// Call from a background thread: UI Automation talks to Explorer.
/// </summary>
internal static class UniKey
{
    public static bool IsRunning => Process.GetProcessesByName("UniKeyNT").Length > 0 || Process.GetProcessesByName("UniKey").Length > 0;

    /// <summary>True for Vietnamese, false for English, null when the icon is not on the taskbar.</summary>
    public static bool? Vietnamese()
    {
        var icon = Find(Taskbar());
        return icon == null ? null : ModeFromTooltip(icon.Current.Name);
    }

    /// <summary>Clicks UniKey's tray icon and returns the new mode when it can be read.</summary>
    public static bool? Toggle()
    {
        if (Find(Taskbar()) is { } icon)
        {
            Invoke(icon);
            Thread.Sleep(150);
            return Vietnamese();
        }

        // The icon lives in the hidden-icons flyout, which only exists while open.
        var taskbar = Taskbar();
        var chevron = taskbar?.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon")));
        if (chevron == null)
            return null;
        Invoke(chevron);
        try
        {
            for (var i = 0; i < 15; i++)
            {
                Thread.Sleep(80);
                // The flyout's icons are not under its own window in the automation
                // tree, so they are looked up from the desktop once it is open.
                var flyout = Native.FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                if (flyout != IntPtr.Zero && Native.IsWindowVisible(flyout) && Find(AutomationElement.RootElement) is { } hidden)
                {
                    var wasVietnamese = ModeFromTooltip(hidden.Current.Name);
                    Invoke(hidden);
                    return wasVietnamese is { } v ? !v : null;
                }
            }
            return null;
        }
        finally
        {
            // Close the flyout again if it is still open.
            if (Native.FindWindow("TopLevelWindowForOverflowXamlIsland", null) is var open && open != IntPtr.Zero
                && Native.IsWindowVisible(open))
                Invoke(chevron);
        }
    }

    private static AutomationElement? Taskbar()
    {
        var hwnd = Native.FindWindow("Shell_TrayWnd", null);
        return hwnd == IntPtr.Zero ? null : AutomationElement.FromHandle(hwnd);
    }

    private static AutomationElement? Find(AutomationElement? root)
    {
        if (root == null)
            return null;
        foreach (AutomationElement item in root.FindAll(TreeScope.Descendants,
                     new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon")))
        {
            if (ModeFromTooltip(item.Current.Name) != null)
                return item;
        }
        return null;
    }

    /// <summary>
    /// Understands UniKey's English and Vietnamese tooltips. Right after a switch
    /// Windows reports the old and new tooltip joined together
    /// ("…turn off Vietnamese mode …turn on Vietnamese mode"), so the phrase
    /// that appears last is the current one.
    /// </summary>
    private static bool? ModeFromTooltip(string tooltip)
    {
        var text = tooltip.ToLowerInvariant();
        var off = Math.Max(text.LastIndexOf("turn off vietnamese", StringComparison.Ordinal), text.LastIndexOf("tắt tiếng việt", StringComparison.Ordinal));
        var on = Math.Max(text.LastIndexOf("turn on vietnamese", StringComparison.Ordinal), text.LastIndexOf("bật tiếng việt", StringComparison.Ordinal));
        if (off < 0 && on < 0)
            return null;
        return off > on;
    }

    private static void Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            ((InvokePattern)pattern).Invoke();
    }
}
