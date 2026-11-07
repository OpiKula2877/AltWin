using System.Runtime.InteropServices;
using System.Windows.Interop;
using AltAccounts.Core.Input;
using AltAccounts.Core.Native;

namespace AltAccounts.App.Input;

/// <summary>
/// Hidden message-only window for RegisterHotKey (custom trigger, idea 4)
/// and WTS session notifications (lock state, idea 11 sign-in/out notifications).
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA17;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _hotkeyRegistered;

    public event Action? HotkeyPressed;
    /// <summary>(event code, session id)</summary>
    public event Action<int, int>? SessionChanged;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("AltAccountsMessages")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        Win32.WTSRegisterSessionNotification(_source.Handle, Win32.NOTIFY_FOR_ALL_SESSIONS);
    }

    /// <summary>Registers the trigger hotkey. Returns an error message, or null on success.</summary>
    public string? SetHotkey(HotkeyParser.Hotkey? hotkey)
    {
        ClearHotkey();
        if (hotkey is null) return null;
        if (RegisterHotKey(_source.Handle, HotkeyId, hotkey.Modifiers | HotkeyParser.MOD_NOREPEAT, hotkey.Vk))
        {
            _hotkeyRegistered = true;
            return null;
        }
        int err = Marshal.GetLastWin32Error();
        return err == ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"{hotkey.Text} is already used by another app. Pick a different shortcut."
            : $"Windows refused {hotkey.Text} (error {err}).";
    }

    public void ClearHotkey()
    {
        if (_hotkeyRegistered) UnregisterHotKey(_source.Handle, HotkeyId);
        _hotkeyRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        else if (msg == Win32.WM_WTSSESSION_CHANGE)
        {
            SessionChanged?.Invoke(wParam.ToInt32(), lParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        ClearHotkey();
        Win32.WTSUnRegisterSessionNotification(_source.Handle);
        _source.Dispose();
    }
}
