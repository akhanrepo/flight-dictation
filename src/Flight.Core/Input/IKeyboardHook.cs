namespace Flight.Core.Input;

/// <param name="VirtualKey">Side-specific code, as a low-level hook reports it (Right Alt is 0xA5).</param>
/// <param name="IsDown">Key-down (including auto-repeat) rather than key-up.</param>
/// <param name="IsInjected">Synthesized by software (SendInput), not typed.</param>
public readonly record struct KeyEvent(int VirtualKey, bool IsDown, bool IsInjected = false);

/// <summary>
/// The system-wide keyboard feed. Implemented by the shell's low-level hook;
/// all decisions about what the keys mean live in <see cref="PushToTalkHotkey"/>.
/// </summary>
public interface IKeyboardHook : IDisposable
{
    /// <param name="onKey">Called for every key event, on the hook thread. Returns true to swallow the key.</param>
    void Start(Func<KeyEvent, bool> onKey);

    /// <summary>
    /// Whether a key is physically down right now. Used to drop keys whose
    /// key-up was never seen (e.g. Win+L locked the screen mid-press).
    /// </summary>
    bool IsKeyDown(int virtualKey);

    /// <summary>Taps <see cref="VirtualKeys.MenuMask"/> so a held Alt or Win won't open a menu on release.</summary>
    void SuppressMenuActivation();
}
