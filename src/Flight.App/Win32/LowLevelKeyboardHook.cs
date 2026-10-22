using System.Diagnostics;
using System.Runtime.InteropServices;
using Flight.Core.Input;

namespace Flight.App.Win32;

/// <summary>
/// The system-wide keyboard feed via a WH_KEYBOARD_LL hook (RegisterHotKey has
/// no key-up notification, which push-to-talk needs). Must be installed on a
/// thread with a message pump — the WinUI UI thread. It only forwards events;
/// <see cref="PushToTalkHotkey"/> decides what they mean and which to swallow.
/// </summary>
public sealed class LowLevelKeyboardHook : IKeyboardHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // Keeps the delegate alive for the lifetime of the hook (GC would otherwise collect it)
    private readonly HookProc _hookProc;
    private IntPtr _hookHandle;
    private Func<KeyEvent, bool>? _onKey;

    public LowLevelKeyboardHook()
    {
        _hookProc = HookCallback;
    }

    public void Start(Func<KeyEvent, bool> onKey)
    {
        _onKey = onKey;
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        using var module = Process.GetCurrentProcess().MainModule!; // always set for the running exe
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to install keyboard hook (error {Marshal.GetLastWin32Error()}).");
        }
    }

    public bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void SuppressMenuActivation()
    {
        // A key event between Alt/Win down and up stops Windows treating the
        // release as "open the menu". 0xE8 is unassigned, so nothing else reacts.
        var inputs = new[]
        {
            KeyInput(VirtualKeys.MenuMask, 0),
            KeyInput(VirtualKeys.MenuMask, KEYEVENTF_KEYUP),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(int vk, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = flags } },
    };

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _onKey is { } onKey)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var message = wParam.ToInt32();
            var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            if (isDown || message is WM_KEYUP or WM_SYSKEYUP)
            {
                var injected = (data.flags & LLKHF_INJECTED) != 0;
                if (onKey(new KeyEvent((int)data.vkCode, isDown, injected)))
                {
                    // Swallowed: other apps never see this key.
                    return 1;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // Sized for the largest member (MOUSEINPUT) so Marshal.SizeOf<INPUT>()
    // matches what SendInput expects.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
