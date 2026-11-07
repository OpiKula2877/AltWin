using System.ComponentModel;
using System.Runtime.InteropServices;
using AltAccounts.Core.Input;

namespace AltAccounts.App.Input;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL) on its own thread with its own message loop,
/// so a busy UI thread can never make Windows drop the hook. It only watches; it never blocks keys.
/// It sees input of this signed-in session only (not the lock or sign-in screen).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX, ptY;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private readonly LowLevelKeyboardProc _proc; // kept alive for the lifetime of the hook
    private readonly AltTapDetector _detector = new();
    private readonly Action _onTap;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;

    public KeyboardHook(Action onTap)
    {
        _onTap = onTap;
        _proc = Callback;
    }

    public bool IncludeRightAlt
    {
        get => _detector.IncludeRightAlt;
        set => _detector.IncludeRightAlt = value;
    }

    public void ResetState() => _detector.Reset();

    public void Start()
    {
        var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) error = new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            if (_hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        { IsBackground = true, Name = "AltAccounts keyboard hook" };
        _thread.Start();
        ready.Wait();
        if (error is not null) throw error;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if ((k.flags & LLKHF_INJECTED) == 0)
                {
                    int msg = (int)wParam;
                    int vk = (int)k.vkCode;
                    if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (vk is AltTapDetector.VK_LMENU or AltTapDetector.VK_RMENU)
                            _detector.PruneHeld(v => (GetAsyncKeyState(v) & 0x8000) != 0);
                        _detector.OnKeyDown(vk, k.time);
                    }
                    else if (msg is WM_KEYUP or WM_SYSKEYUP)
                    {
                        if (_detector.OnKeyUp(vk, k.time)) _onTap();
                    }
                }
            }
            catch (Exception) { /* never let an exception escape into the hook chain */ }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
        _thread = null;
    }
}
