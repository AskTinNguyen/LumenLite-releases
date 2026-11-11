using System;
using System.Runtime.InteropServices;

namespace LumenLite;

/// <summary>
/// Per-app output and input devices ("App volume and device preferences" in
/// Windows Settings). Windows keeps this behind an internal WinRT factory, the
/// same one EarTrumpet uses. Apps pick the change up when they next open an
/// audio stream; many need a pause/play or a restart.
/// </summary>
internal static class AudioPolicy
{
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string CaptureSuffix = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";
    private const string Prefix = @"\\?\SWD#MMDEVAPI#";

    private static IAudioPolicyConfigFactory? _factory;
    private static bool _unavailable;

    public static bool Available => Factory() != null;

    /// <summary>The device the app is pinned to, or null when it follows the Windows default.</summary>
    public static string? GetOutput(int pid)
    {
        var factory = Factory();
        if (factory == null)
            return null;
        if (factory.GetPersistedDefaultAudioEndpoint((uint)pid, 0, 1, out var hstring) != 0 || hstring == IntPtr.Zero)
            return null;
        try
        {
            var raw = WindowsGetStringRawBuffer(hstring, out var length);
            var full = Marshal.PtrToStringUni(raw, (int)length);
            return Unwrap(full);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    /// <summary>Pins an app to an output device; null puts it back on the Windows default.</summary>
    public static bool SetOutput(int pid, string? deviceId)
    {
        var factory = Factory();
        if (factory == null)
            return false;
        var hstring = IntPtr.Zero;
        try
        {
            if (deviceId != null)
            {
                var full = Prefix + deviceId + RenderSuffix;
                WindowsCreateString(full, full.Length, out hstring);
            }
            // Console and multimedia roles, like the Settings page.
            var a = factory.SetPersistedDefaultAudioEndpoint((uint)pid, 0, 0, hstring);
            var b = factory.SetPersistedDefaultAudioEndpoint((uint)pid, 0, 1, hstring);
            return a == 0 && b == 0;
        }
        finally
        {
            if (hstring != IntPtr.Zero)
                WindowsDeleteString(hstring);
        }
    }

    private static string? Unwrap(string? full)
    {
        if (string.IsNullOrEmpty(full))
            return null;
        var id = full;
        if (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            id = id[Prefix.Length..];
        var hash = id.LastIndexOf('#');
        return hash > 0 ? id[..hash] : id;
    }

    private static IAudioPolicyConfigFactory? Factory()
    {
        if (_factory != null || _unavailable)
            return _factory;
        try
        {
            const string name = "Windows.Media.Internal.AudioPolicyConfig";
            WindowsCreateString(name, name.Length, out var hstring);
            try
            {
                var iid = typeof(IAudioPolicyConfigFactory).GUID;
                if (RoGetActivationFactory(hstring, ref iid, out var pointer) == 0 && pointer != IntPtr.Zero)
                {
                    _factory = (IAudioPolicyConfigFactory)Marshal.GetObjectForIUnknown(pointer);
                    Marshal.Release(pointer);
                }
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }
        catch (Exception ex)
        {
            Store.Log("audio policy unavailable: " + ex.Message);
        }
        _unavailable = _factory == null;
        return _factory;
    }

    /// <summary>
    /// Windows 10 21H2 and later. Declared as IUnknown with the three IInspectable
    /// slots first, since .NET no longer has built-in IInspectable support.
    /// </summary>
    [ComImport, Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioPolicyConfigFactory
    {
        void GetIids();
        void GetRuntimeClassName();
        void GetTrustLevel();
        void AddCtxVolumeChange();
        void RemoveCtxVolumeChanged();
        void AddRingerVibrateStateChanged();
        void RemoveRingerVibrateStateChange();
        void SetVolumeGroupGainForId();
        void GetVolumeGroupGainForId();
        void GetActiveVolumeGroupForEndpointId();
        void GetVolumeGroupsForEndpoint();
        void GetCurrentVolumeContext();
        void SetVolumeGroupMuteForId();
        void GetVolumeGroupMuteForId();
        void SetRingerVibrateState();
        void GetRingerVibrateState();
        void SetPreferredChatApplication();
        void ResetPreferredChatApplication();
        void GetPreferredChatApplication();
        void GetCurrentChatApplications();
        void AddChatContextChanged();
        void RemoveChatContextChanged();
        [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceId);
        [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceId);
        [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
    }

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);
}
