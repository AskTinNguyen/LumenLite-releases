using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace LumenLite;

/// <summary>
/// Hold the middle mouse button to open the radial menu, let go to pick.
/// The press is held back for a quarter of a second: a quick click, or a press
/// that starts dragging, is replayed to the app under the pointer, so middle-click
/// (open in new tab, autoscroll, pan) keeps working. Full-screen games and videos
/// are left alone entirely.
/// </summary>
internal sealed class MiddleHold : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    private const uint LLMHF_INJECTED = 0x1;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const int MoveTolerance = 10;

    private readonly HookProc _proc;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    // The hook lives on its own thread with its own message loop: every mouse event in Windows
    // waits for this callback, so it must never queue behind LumenLite's interface work.
    private Thread? _thread;
    private Dispatcher? _hookDispatcher;
    private DispatcherTimer? _hold;
    private IntPtr _hook;
    private bool _pending, _open;
    private POINT _start;

    /// <summary>Opens the menu around the pointer.</summary>
    public Action? Open { get; set; }

    /// <summary>The button was let go while the menu was open: run what is pointed at.</summary>
    public Action? Release { get; set; }

    public MiddleHold()
    {
        _proc = Callback;
    }

    public bool Enabled
    {
        get => _thread != null;
        set
        {
            if (value == Enabled) return;
            if (value)
                StartThread();
            else
                StopThread();
        }
    }

    private void StartThread()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _hookDispatcher = Dispatcher.CurrentDispatcher;
            _hold = new DispatcherTimer(DispatcherPriority.Send, _hookDispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
            _hold.Tick += (_, _) =>
            {
                _hold.Stop();
                if (!_pending) return;
                _pending = false;
                _open = true;
                _ui.BeginInvoke(() => Open?.Invoke());
            };
            using (var module = Process.GetCurrentProcess().MainModule!)
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module.ModuleName), 0);
            if (_hook == IntPtr.Zero)
                Store.Log("middle-hold hook failed: " + Marshal.GetLastWin32Error());
            ready.Set();
            Dispatcher.Run();
            if (_hook != IntPtr.Zero)
                UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "LumenLite mouse hook",
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(2000);
    }

    private void StopThread()
    {
        _pending = _open = false;
        _hookDispatcher?.InvokeShutdown();
        _thread?.Join(1000);
        _thread = null;
        _hookDispatcher = null;
    }

    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        // Called for every mouse event in the system (1000 a second with gaming mice):
        // anything that is not a middle-button event or a move during a pending press leaves at once.
        var msg = message.ToInt32();
        if (code < 0 || (msg != WM_MBUTTONDOWN && msg != WM_MBUTTONUP && !(msg == WM_MOUSEMOVE && _pending)))
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(data);
        // Our own replays (and other software's injected input) pass straight through.
        if ((info.flags & LLMHF_INJECTED) != 0)
            return CallNextHookEx(IntPtr.Zero, code, message, data);

        switch (message.ToInt32())
        {
            case WM_MBUTTONDOWN:
                if (FullScreen.Foreground())
                    break;
                _pending = true;
                _start = info.pt;
                _hold?.Stop();
                _hold?.Start();
                return new IntPtr(1);

            case WM_MOUSEMOVE when _pending:
                if (Math.Abs(info.pt.X - _start.X) > MoveTolerance || Math.Abs(info.pt.Y - _start.Y) > MoveTolerance)
                {
                    // A drag (autoscroll, panning): give the press back and step aside.
                    _pending = false;
                    _hold?.Stop();
                    Replay(MOUSEEVENTF_MIDDLEDOWN);
                }
                break;

            case WM_MBUTTONUP:
                if (_pending)
                {
                    // A normal click: replay it whole.
                    _pending = false;
                    _hold?.Stop();
                    Replay(MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
                    return new IntPtr(1);
                }
                if (_open)
                {
                    _open = false;
                    _ui.BeginInvoke(() => Release?.Invoke());
                    return new IntPtr(1);
                }
                break;
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    /// <summary>Sends the events after the hook returns, so they arrive in order.</summary>
    private static void Replay(params uint[] flags) => Dispatcher.CurrentDispatcher.BeginInvoke(() =>
    {
        var inputs = new INPUT[flags.Length];
        for (var i = 0; i < flags.Length; i++)
            inputs[i] = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = flags[i] } };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    });

    public void Dispose() => Enabled = false;

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>INPUT with only the mouse member; sized for the union's largest member on x64.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
