using System.Diagnostics;
using System.Runtime.InteropServices;
using Flight.Core;
using Flight.Core.Input;

namespace Flight.App.Win32;

/// <summary>
/// Global push-to-talk key via a WH_KEYBOARD_LL hook (RegisterHotKey has no
/// key-up notification, which push-to-talk needs). Must be installed on a
/// thread with a message pump — the WinUI UI thread. Key events pass through
/// to other apps; auto-repeat key-downs are collapsed into one Pressed.
/// </summary>
public sealed class LowLevelKeyboardHook : IPushToTalkHotkey
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly int _virtualKey;
    // Keeps the delegate alive for the lifetime of the hook (GC would otherwise collect it)
    private readonly HookProc _hookProc;
    private IntPtr _hookHandle;
    private bool _keyIsDown;

    public event Action? Pressed;
    public event Action? Released;

    public LowLevelKeyboardHook(FlightOptions options)
    {
        _virtualKey = options.PushToTalkVirtualKey;
        _hookProc = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        using var module = Process.GetCurrentProcess().MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to install keyboard hook (error {Marshal.GetLastWin32Error()}).");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == _virtualKey)
            {
                var message = wParam.ToInt32();
                if ((message == WM_KEYDOWN || message == WM_SYSKEYDOWN) && !_keyIsDown)
                {
                    _keyIsDown = true;
                    Pressed?.Invoke();
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    _keyIsDown = false;
                    Released?.Invoke();
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
