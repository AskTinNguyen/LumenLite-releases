using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LumenLite;

public sealed record AudioOutput(string Id, string Name, AudioFormFactor FormFactor);

/// <summary>An app's own volume in the mixer. Pid 0 stands for Windows' system sounds.</summary>
public sealed record AppVolume(int Pid, bool IsSystem, string? DisplayName, float Level, bool Muted);

public enum AudioFormFactor
{
    // Values of the Windows EndpointFormFactor enumeration.
    Other = 0,
    Speakers = 1,
    Headphones = 3,
    Headset = 5,
    Spdif = 8,
    Display = 9, // HDMI / DisplayPort audio
}

/// <summary>
/// Lists playback devices and changes the Windows default one. Windows has no
/// public API for the latter; like EarTrumpet and SoundSwitch this uses the
/// long-standing IPolicyConfig interface behind the Sound control panel.
/// </summary>
internal static class Audio
{
    private const int eRender = 0;
    private const int eCapture = 1;
    private const int DEVICE_STATE_ACTIVE = 1;
    private const int STGM_READ = 0;
    private const int CLSCTX_ALL = 23;
    private const ushort VT_LPWSTR = 31;
    private const ushort VT_UI4 = 19;

    private static PROPERTYKEY FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static PROPERTYKEY FormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

    public static List<AudioOutput> Outputs() => Devices(eRender);

    /// <summary>Microphones and other recording devices.</summary>
    public static List<AudioOutput> Inputs() => Devices(eCapture);

    public static string? DefaultInputId() => WithDefault(device =>
    {
        device.GetId(out var id);
        return id;
    }, eCapture);

    private static List<AudioOutput> Devices(int dataFlow)
    {
        var result = new List<AudioOutput>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out var devices));
            devices.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                devices.Item(i, out var device);
                try
                {
                    device.GetId(out var id);
                    device.OpenPropertyStore(STGM_READ, out var store);
                    try
                    {
                        var name = ReadString(store, FriendlyName) ?? id;
                        var formFactor = (AudioFormFactor)(ReadUInt(store, FormFactor) ?? 0);
                        result.Add(new AudioOutput(id, name, formFactor));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(store);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            Marshal.ReleaseComObject(devices);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        return result;
    }

    public static string? DefaultId() => WithDefault(device =>
    {
        device.GetId(out var id);
        return id;
    });

    /// <summary>Master volume of the default output, 0..1, or null when there is no output.</summary>
    public static (float Level, bool Muted)? Volume() => WithVolume(volume =>
    {
        volume.GetMasterVolumeLevelScalar(out var level);
        volume.GetMute(out var muted);
        return ((float, bool)?)(level, muted);
    });

    public static void SetVolume(float level) => WithVolume(volume =>
    {
        var context = Guid.Empty;
        volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context);
        if (level > 0)
            volume.SetMute(false, ref context);
        return true;
    });

    public static void SetMute(bool muted) => WithVolume(volume =>
    {
        var context = Guid.Empty;
        volume.SetMute(muted, ref context);
        return true;
    });

    private static T? WithDefault<T>(Func<IMMDevice, T?> action, int dataFlow = eRender)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            // 1 = eMultimedia, the role the volume flyout shows as the output device;
            // for capture, 2 = eCommunications, the microphone calls use.
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, dataFlow == eRender ? 1 : 2, out var device) != 0)
                return default;
            try
            {
                return action(device);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static T? WithVolume<T>(Func<IAudioEndpointVolume, T?> action, int dataFlow = eRender) => WithDefault(device =>
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var pointer) != 0)
            return default;
        var volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            return action(volume);
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
            Marshal.Release(pointer);
        }
    }, dataFlow);

    // ---- Output level meter, for the Dynamic Island bars ----

    private static IAudioMeterInformation? _meter;
    private static IntPtr _meterPointer;
    private static DateTime _meterOpened;

    /// <summary>Peak level (0..1) of what the default output is playing right now. UI thread only.</summary>
    public static float Peak()
    {
        try
        {
            // Re-opened every few seconds so a change of default device is followed.
            if (_meter == null || DateTime.UtcNow - _meterOpened > TimeSpan.FromSeconds(3))
            {
                CloseMeter();
                WithDefault(device =>
                {
                    var iid = typeof(IAudioMeterInformation).GUID;
                    if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var pointer) != 0)
                        return false;
                    _meterPointer = pointer;
                    _meter = (IAudioMeterInformation)Marshal.GetObjectForIUnknown(pointer);
                    return true;
                });
                _meterOpened = DateTime.UtcNow;
            }
            return _meter != null && _meter.GetPeakValue(out var peak) == 0 ? peak : 0;
        }
        catch
        {
            CloseMeter();
            return 0;
        }
    }

    private static void CloseMeter()
    {
        if (_meter != null)
            Marshal.ReleaseComObject(_meter);
        if (_meterPointer != IntPtr.Zero)
            Marshal.Release(_meterPointer);
        _meter = null;
        _meterPointer = IntPtr.Zero;
    }

    /// <summary>Switches the default output to the next device in name order; returns its name.</summary>
    public static string? NextOutput()
    {
        var outputs = Outputs();
        if (outputs.Count < 2)
            return outputs.Count == 1 ? outputs[0].Name : null;
        outputs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        var current = outputs.FindIndex(o => o.Id == DefaultId());
        var next = outputs[(current + 1) % outputs.Count];
        SetDefault(next.Id);
        return next.Name;
    }

    // ---- Microphone (default capture device) ----

    public static (float Level, bool Muted)? MicVolume() => WithVolume(volume =>
    {
        volume.GetMasterVolumeLevelScalar(out var level);
        volume.GetMute(out var muted);
        return ((float, bool)?)(level, muted);
    }, eCapture);

    public static void SetMicVolume(float level) => WithVolume(volume =>
    {
        var context = Guid.Empty;
        volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context);
        return true;
    }, eCapture);

    public static void SetMicMute(bool muted) => WithVolume(volume =>
    {
        var context = Guid.Empty;
        volume.SetMute(muted, ref context);
        return true;
    }, eCapture);

    // ---- Per-app volume (audio sessions of the default output) ----

    // Some Windows builds expose a session's volume only per channel (no ISimpleAudioVolume,
    // so no mute flag either). There, mute is done by setting the channels to 0 and
    // remembering the level to restore.
    private static readonly Dictionary<int, float> MutedLevels = new();

    /// <summary>One entry per process playing on the default output; pid 0 is Windows' system sounds.</summary>
    public static List<AppVolume> Apps()
    {
        var byPid = new Dictionary<int, AppVolume>();
        ForEachSession((pid, isSystem, displayName, session) =>
        {
            if (byPid.ContainsKey(pid))
                return;
            var (level, muted) = Read(pid, session);
            byPid[pid] = new AppVolume(pid, isSystem, displayName, level, muted);
        });
        lock (MutedLevels)
        {
            foreach (var pid in new List<int>(MutedLevels.Keys))
                if (!byPid.ContainsKey(pid))
                    MutedLevels.Remove(pid);
        }
        return new List<AppVolume>(byPid.Values);
    }

    /// <summary>Sets every session of the process, since browsers open several.</summary>
    public static void SetAppVolume(int pid, float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        lock (MutedLevels)
            MutedLevels.Remove(pid);
        ForEachSession((p, _, _, session) =>
        {
            if (p != pid) return;
            var context = Guid.Empty;
            if (session is ISimpleAudioVolume simple)
            {
                simple.SetMasterVolume(level, ref context);
                if (level > 0)
                    simple.SetMute(false, ref context);
            }
            else if (session is IChannelAudioVolume channels)
            {
                SetChannels(channels, level);
            }
        });
    }

    public static void SetAppMute(int pid, bool muted) => ForEachSession((p, _, _, session) =>
    {
        if (p != pid) return;
        var context = Guid.Empty;
        if (session is ISimpleAudioVolume simple)
        {
            simple.SetMute(muted, ref context);
        }
        else if (session is IChannelAudioVolume channels)
        {
            lock (MutedLevels)
            {
                if (muted)
                {
                    if (!MutedLevels.ContainsKey(pid))
                        MutedLevels[pid] = ChannelLevel(channels);
                    SetChannels(channels, 0);
                }
                else
                {
                    var restore = MutedLevels.TryGetValue(pid, out var saved) ? saved : 1f;
                    MutedLevels.Remove(pid);
                    SetChannels(channels, restore > 0 ? restore : 1f);
                }
            }
        }
    });

    private static (float Level, bool Muted) Read(int pid, object session)
    {
        if (session is ISimpleAudioVolume simple)
        {
            simple.GetMasterVolume(out var level);
            simple.GetMute(out var muted);
            return (level, muted);
        }
        if (session is IChannelAudioVolume channels)
        {
            lock (MutedLevels)
            {
                if (MutedLevels.TryGetValue(pid, out var saved))
                    return (saved, true);
            }
            return (ChannelLevel(channels), false);
        }
        return (1f, false);
    }

    private static float ChannelLevel(IChannelAudioVolume channels)
    {
        if (channels.GetChannelCount(out var count) != 0 || count == 0)
            return 1f;
        float max = 0;
        for (uint i = 0; i < count; i++)
        {
            if (channels.GetChannelVolume(i, out var level) == 0)
                max = Math.Max(max, level);
        }
        return max;
    }

    private static void SetChannels(IChannelAudioVolume channels, float level)
    {
        if (channels.GetChannelCount(out var count) != 0)
            return;
        var context = Guid.Empty;
        for (uint i = 0; i < count; i++)
            channels.SetChannelVolume(i, level, ref context);
    }

    private static void ForEachSession(Action<int, bool, string?, object> action) => WithDefault(device =>
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var pointer) != 0)
            return false;
        var manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(pointer);
        try
        {
            if (manager.GetSessionEnumerator(out var sessions) != 0)
                return false;
            try
            {
                sessions.GetCount(out var count);
                for (var i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var control) != 0)
                        continue;
                    try
                    {
                        // 2 = AudioSessionStateExpired: the app has gone.
                        if (control.GetState(out var state) != 0 || state == 2)
                            continue;
                        control.GetProcessId(out var pid);
                        var isSystem = control.IsSystemSoundsSession() == 0;
                        control.GetDisplayName(out var name);
                        action(isSystem ? 0 : (int)pid, isSystem, name, control);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(control);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(sessions);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(manager);
            Marshal.Release(pointer);
        }
        return true;
    });

    /// <summary>Makes the device the default for every role (console, multimedia, communications).</summary>
    public static void SetDefault(string id)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            for (var role = 0; role < 3; role++)
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    private static string? ReadString(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out var value) != 0)
            return null;
        try
        {
            return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static uint? ReadUInt(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out var value) != 0)
            return null;
        try
        {
            return value.vt == VT_UI4 ? value.uintValue : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;

        public PROPERTYKEY(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
        [FieldOffset(8)] public uint uintValue;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    /// <summary>Slots up to GetMute, in vtable order; unused ones are placeholders.</summary>
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6F7AC0AC9D52"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IChannelAudioVolume
    {
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetChannelVolume(uint index, float level, ref Guid context);
        [PreserveSig] int GetChannelVolume(uint index, out float level);
    }

    /// <summary>Only SetDefaultEndpoint is called; the earlier slots keep the vtable order.</summary>
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }
}
