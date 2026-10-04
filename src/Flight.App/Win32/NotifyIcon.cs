using System.Runtime.InteropServices;
using Flight.Core.Tray;

namespace Flight.App.Win32;

/// <summary>
/// Notification-area icon via Shell_NotifyIcon. Renders a <see cref="TrayState"/>
/// and reports clicks; every decision lives in <see cref="TrayPresenter"/>.
/// Must be created on a thread with a message pump (the WinUI UI thread).
/// </summary>
/// <remarks>
/// Uses a hidden top-level window rather than a message-only one: only
/// top-level windows receive the "TaskbarCreated" broadcast, which is how
/// the icon comes back after Explorer restarts.
/// </remarks>
public sealed class NotifyIcon : IDisposable
{
    private const int WM_APP_TRAY = 0x8000 + 1; // WM_APP + 1
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_NULL = 0x0000;
    private const int NIN_SELECT = 0x0400;
    private const int NIN_KEYSELECT = 0x0401;

    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int NIIF_USER = 0x04, NIIF_LARGE_ICON = 0x20;
    private const int NOTIFYICON_VERSION_4 = 4;

    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;

    private const uint IconId = 1;
    private static readonly string ClassName = "FlightTrayWindow";

    private readonly WndProc _wndProc; // kept alive for the window's lifetime
    private readonly Func<TrayIconKind, string> _iconPath;
    private readonly Dictionary<TrayIconKind, IntPtr> _icons = [];
    private readonly uint _taskbarCreated;
    private IntPtr _hwnd;
    private TrayState? _state;
    private bool _added;

    /// <summary>Left-click (or Enter/Space on the focused icon).</summary>
    public event Action? Activated;

    /// <summary>Raised just before the menu opens, so the presenter can refresh live state.</summary>
    public event Action? MenuOpening;

    public event Action<TrayCommand>? CommandInvoked;

    public NotifyIcon(Func<TrayIconKind, string> iconPath)
    {
        _iconPath = iconPath;
        _wndProc = WindowProc;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        var hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        RegisterClassExW(ref wc); // fails harmlessly if already registered

        _hwnd = CreateWindowExW(0, ClassName, "Flight", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Couldn't create the tray window (error {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>Adds the icon on first call, then updates it in place.</summary>
    public void Render(TrayState state)
    {
        _state = state;
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        data.hIcon = Icon(state.Icon);
        data.szTip = state.Tooltip;

        if (!_added)
        {
            _added = Shell_NotifyIconW(NIM_ADD, ref data);
            data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, ref data);
        }
        else
        {
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
    }

    /// <summary>A Windows notification attributed to the tray icon.</summary>
    public void ShowNotification(string title, string message)
    {
        if (!_added)
        {
            return;
        }

        var data = NewData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
        data.hBalloonIcon = Icon(TrayIconKind.Normal);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }

        foreach (var icon in _icons.Values)
        {
            DestroyIcon(icon);
        }
        _icons.Clear();

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private NOTIFYICONDATAW NewData(int flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr Icon(TrayIconKind kind)
    {
        if (!_icons.TryGetValue(kind, out var handle))
        {
            // Small-icon size at the system DPI, picked from the .ico's frames.
            var dpi = GetDpiForSystem();
            var size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
            handle = LoadImageW(IntPtr.Zero, _iconPath(kind), IMAGE_ICON, size, size, LR_LOADFROMFILE);
            _icons[kind] = handle;
        }
        return handle;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            // NOTIFYICON_VERSION_4: the event is in LOWORD(lParam), the
            // anchor point in wParam.
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    Activated?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    var x = (short)(wParam.ToInt64() & 0xFFFF);
                    var y = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                    ShowMenu(x, y);
                    break;
            }
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreated && _state is not null)
        {
            _added = false; // Explorer restarted; the old icon is gone
            Render(_state);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        MenuOpening?.Invoke();
        if (_state is null)
        {
            return;
        }

        var menu = CreatePopupMenu();
        try
        {
            var commands = new List<TrayCommand>();
            foreach (var item in _state.Menu.Where(i => i.Visible))
            {
                if (item.IsSeparator)
                {
                    AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }

                commands.Add(item.Command);
                var flags = MF_STRING | (item.Enabled ? 0 : MF_GRAYED) | (item.Checked ? MF_CHECKED : 0);
                // '&' marks a mnemonic in menus; escape it so "Account & subscription" shows literally.
                AppendMenuW(menu, flags, (UIntPtr)commands.Count, item.Text.Replace("&", "&&"));
            }

            // Left-click opens Settings, so mark it as the default (bold) item.
            var settingsIndex = commands.IndexOf(TrayCommand.Settings);
            if (settingsIndex >= 0)
            {
                SetMenuDefaultItem(menu, (uint)settingsIndex + 1, 0);
            }

            // Required for the menu to close when the user clicks elsewhere.
            SetForegroundWindow(_hwnd);
            var chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, x, y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (chosen > 0 && chosen <= commands.Count)
            {
                CommandInvoked?.Invoke(commands[chosen - 1]);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private const int SM_CXSMICON = 49;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
