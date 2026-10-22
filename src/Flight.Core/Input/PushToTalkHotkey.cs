using Flight.Core.Settings;

namespace Flight.Core.Input;

/// <summary>The outcome of recording a new hotkey in Settings.</summary>
/// <param name="Hotkey">The combination pressed, valid or not; null when cancelled.</param>
/// <param name="Error">Why <paramref name="Hotkey"/> can't be used, or null when it can.</param>
public sealed record HotkeyRecording(Hotkey? Hotkey, string? Error)
{
    public bool Cancelled => Hotkey is null;
    public bool IsValid => Hotkey is not null && Error is null;
}

/// <summary>
/// Push-to-talk over the raw keyboard feed. Reads the hotkey from settings on
/// every press, so a new key takes effect without a restart; the hotkey that
/// started a dictation is the one that ends it. Also records a new hotkey for
/// the Settings window.
/// </summary>
/// <remarks>
/// Key events arrive on the hook thread, one at a time; nothing here blocks.
/// </remarks>
public sealed class PushToTalkHotkey : IPushToTalkHotkey
{
    private readonly IKeyboardHook _hook;
    private readonly ISettingsStore _settings;
    private readonly HashSet<int> _held = [];

    // Keys whose key-down was swallowed. Exactly these get their key-up
    // swallowed too: a swallowed up after a passed-through down would leave
    // Windows thinking the key is still held.
    private readonly HashSet<int> _swallowed = [];
    private readonly object _lock = new();

    private Hotkey? _active;
    private Action<HotkeyRecording>? _recording;
    private readonly HashSet<int> _recorded = [];

    public PushToTalkHotkey(IKeyboardHook hook, ISettingsStore settings)
    {
        _hook = hook;
        _settings = settings;
    }

    public event Action? Pressed;
    public event Action? Released;

    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _recording is not null;
            }
        }
    }

    public void Start() => _hook.Start(OnKey);

    /// <summary>
    /// The next combination pressed is reported to <paramref name="completed"/>
    /// (on the hook thread) instead of starting a dictation. Esc cancels.
    /// </summary>
    public void BeginRecording(Action<HotkeyRecording> completed)
    {
        lock (_lock)
        {
            _recording = completed;
            _recorded.Clear();
        }
    }

    public void CancelRecording()
    {
        Action<HotkeyRecording>? callback;
        lock (_lock)
        {
            callback = _recording;
            _recording = null;
        }

        callback?.Invoke(new HotkeyRecording(null, null));
    }

    /// <returns>True to swallow the key.</returns>
    internal bool OnKey(KeyEvent key)
    {
        if (key.IsInjected)
        {
            // Our own menu-mask taps, and other software's synthetic input.
            return false;
        }

        Action? raise = null;
        Action? complete = null;
        bool swallow;

        lock (_lock)
        {
            var repeat = key.IsDown && !_held.Add(key.VirtualKey);
            if (!key.IsDown)
            {
                _held.Remove(key.VirtualKey);
            }

            if (key.IsDown)
            {
                if (_recording is not null)
                {
                    complete = Record(key, repeat);
                    swallow = true;
                }
                else
                {
                    swallow = OnDown(key.VirtualKey, repeat, out raise);
                }

                if (swallow)
                {
                    _swallowed.Add(key.VirtualKey);
                }
            }
            else
            {
                if (_recording is not null)
                {
                    complete = Record(key, repeat);
                }
                else
                {
                    raise = OnUp(key.VirtualKey);
                }

                swallow = _swallowed.Remove(key.VirtualKey);
            }
        }

        // Outside the lock: handlers start and stop the microphone.
        raise?.Invoke();
        complete?.Invoke();
        return swallow;
    }

    private bool OnDown(int vk, bool repeat, out Action? raise)
    {
        raise = null;
        if (_active is not null)
        {
            // Auto-repeat while held: already pressed. Keep swallowing the
            // non-modifier key so it doesn't type.
            return SwallowsKey(_active, vk);
        }

        if (repeat)
        {
            return false;
        }

        DropStaleKeys(vk);

        var hotkey = _settings.Current.Hotkey;
        if (!hotkey.IsMatchedBy(_held))
        {
            return false;
        }

        _active = hotkey;
        if (hotkey.InvolvesMenuKey)
        {
            _hook.SuppressMenuActivation();
        }
        raise = Pressed;
        return SwallowsKey(hotkey, vk);
    }

    private Action? OnUp(int vk)
    {
        if (_active is not { } active || !active.Contains(vk))
        {
            return null;
        }

        _active = null;
        return Released;
    }

    /// <summary>
    /// Only a non-modifier key of the hotkey is swallowed (so Ctrl+Space never
    /// types a space). Modifiers always reach other apps, so none of them is
    /// ever left looking stuck.
    /// </summary>
    private static bool SwallowsKey(Hotkey hotkey, int vk) => !VirtualKeys.IsModifier(vk) && hotkey.Contains(vk);

    private void DropStaleKeys(int current)
    {
        _held.RemoveWhere(vk => vk != current && !_hook.IsKeyDown(vk));
    }

    private Action? Record(KeyEvent key, bool repeat)
    {
        var callback = _recording!;
        if (key.IsDown)
        {
            if (key.VirtualKey == VirtualKeys.Escape && _recorded.Count == 0)
            {
                _recording = null;
                return () => callback(new HotkeyRecording(null, null));
            }

            if (!repeat)
            {
                _recorded.Add(key.VirtualKey);
            }
            return null;
        }

        if (!_recorded.Contains(key.VirtualKey))
        {
            // The key-up of something already down when recording started
            // (Enter on the button, say): not part of the new hotkey.
            return null;
        }

        // First key-up: the combination held at its peak is the new hotkey.
        var hotkey = Hotkey.FromPressedKeys(_recorded);
        _recording = null;
        _recorded.Clear();
        return () => callback(new HotkeyRecording(hotkey, hotkey.Validate()));
    }

    public void Dispose() => _hook.Dispose();
}
