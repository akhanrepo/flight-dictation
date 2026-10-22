namespace Flight.Core.Input;

/// <summary>Windows virtual-key codes Flight's hotkey logic needs by name.</summary>
public static class VirtualKeys
{
    public const int Backspace = 0x08;
    public const int Tab = 0x09;
    public const int Enter = 0x0D;
    public const int Shift = 0x10;
    public const int Ctrl = 0x11;
    public const int Alt = 0x12;
    public const int Pause = 0x13;
    public const int CapsLock = 0x14;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int F1 = 0x70;
    public const int F24 = 0x87;
    public const int ScrollLock = 0x91;
    public const int LeftShift = 0xA0;
    public const int RightShift = 0xA1;
    public const int LeftCtrl = 0xA2;
    public const int RightCtrl = 0xA3;
    public const int LeftAlt = 0xA4;
    public const int RightAlt = 0xA5;

    /// <summary>
    /// Left Win. In a combination it stands for either Win key (there is no
    /// side-neutral Win code).
    /// </summary>
    public const int Win = 0x5B;

    public const int LeftWin = Win;
    public const int RightWin = 0x5C;

    /// <summary>
    /// Unassigned key Windows ignores. Sent while Alt or Win is held so that
    /// releasing them doesn't open a menu bar or the Start menu.
    /// </summary>
    public const int MenuMask = 0xE8;

    /// <summary>The side-neutral modifier a physical key belongs to, or null for other keys.</summary>
    public static int? ModifierOf(int vk) => vk switch
    {
        Shift or LeftShift or RightShift => Shift,
        Ctrl or LeftCtrl or RightCtrl => Ctrl,
        Alt or LeftAlt or RightAlt => Alt,
        LeftWin or RightWin => Win,
        _ => null,
    };

    public static bool IsModifier(int vk) => ModifierOf(vk) is not null;

    public static bool IsRightHandModifier(int vk) => vk is RightShift or RightCtrl or RightAlt or RightWin;
}

/// <summary>
/// The push-to-talk key or key combination. Modifiers in a combination are
/// side-neutral ("Ctrl+Space" matches either Ctrl); a modifier used on its own
/// keeps its side ("Right Alt"). Keys are kept in a canonical order —
/// Ctrl, Alt, Shift, Win, then the rest — so equal hotkeys compare equal.
/// </summary>
public sealed record Hotkey
{
    public const int MaxKeys = 4;

    public static Hotkey Default { get; } = new([VirtualKeys.RightAlt]);

    private static readonly int[] ModifierOrder = [VirtualKeys.Ctrl, VirtualKeys.Alt, VirtualKeys.Shift, VirtualKeys.Win];

    private Hotkey(IReadOnlyList<int> keys) => Keys = keys;

    /// <summary>Virtual-key codes in canonical order.</summary>
    public IReadOnlyList<int> Keys { get; }

    /// <summary>
    /// Builds a hotkey from the physical keys held: one key keeps its exact
    /// code, several have their modifiers made side-neutral.
    /// </summary>
    public static Hotkey FromPressedKeys(IEnumerable<int> virtualKeys)
    {
        var distinct = virtualKeys.Distinct().ToList();
        if (distinct.Count <= 1)
        {
            return new Hotkey(distinct);
        }

        var normalized = distinct.Select(vk => VirtualKeys.ModifierOf(vk) ?? vk).Distinct().ToList();
        if (normalized.Count == 1)
        {
            // Both Ctrl keys, say: still a lone modifier, so keep the first's side.
            return new Hotkey([distinct[0]]);
        }

        var modifiers = ModifierOrder.Where(normalized.Contains);
        var others = normalized.Where(vk => !VirtualKeys.IsModifier(vk)).Order();
        return new Hotkey(modifiers.Concat(others).ToList());
    }

    /// <summary>True when the hotkey includes Alt or Win, whose bare release opens a menu.</summary>
    public bool InvolvesMenuKey => Keys.Any(vk => VirtualKeys.ModifierOf(vk) is VirtualKeys.Alt or VirtualKeys.Win);

    /// <summary>Whether a physical key is one of this hotkey's keys.</summary>
    public bool Contains(int physicalKey) => Keys.Any(key => Satisfies(physicalKey, key));

    /// <summary>
    /// All of this hotkey's keys are held, and no other non-modifier key is.
    /// Extra modifiers are tolerated: AltGr layouts report Right Alt together
    /// with a synthetic Left Ctrl.
    /// </summary>
    public bool IsMatchedBy(IReadOnlyCollection<int> held) =>
        Keys.Count > 0
        && Keys.All(key => held.Any(physical => Satisfies(physical, key)))
        && held.All(physical => VirtualKeys.IsModifier(physical) || Contains(physical));

    private bool Satisfies(int physical, int key) =>
        physical == key
        || (Keys.Count > 1 && VirtualKeys.ModifierOf(physical) is { } modifier && modifier == key);

    /// <returns>Null when valid, otherwise why not, in words for the Settings window.</returns>
    public string? Validate()
    {
        if (Keys.Count == 0)
        {
            return "Press at least one key.";
        }

        if (Keys.Count > MaxKeys)
        {
            return $"Use at most {MaxKeys} keys.";
        }

        if (Keys.Contains(VirtualKeys.Escape))
        {
            return "Esc can't be part of the push-to-talk key.";
        }

        if (Keys.Count == 1)
        {
            var key = Keys[0];
            if (VirtualKeys.IsModifier(key))
            {
                return VirtualKeys.IsRightHandModifier(key)
                    ? null
                    : $"{NameOf(key)} on its own is used all the time. Use a right-hand one, or add another key.";
            }

            return IsStandaloneKey(key)
                ? null
                : $"{NameOf(key)} on its own would stop you typing it. Add Ctrl, Alt or Win.";
        }

        // A combination with a typing key needs a modifier that doesn't type.
        var hasNonShiftModifier = Keys.Any(vk => VirtualKeys.ModifierOf(vk) is VirtualKeys.Ctrl or VirtualKeys.Alt or VirtualKeys.Win);
        var hasTypingKey = Keys.Any(vk => !VirtualKeys.IsModifier(vk) && !IsStandaloneKey(vk));
        return hasTypingKey && !hasNonShiftModifier
            ? "Add Ctrl, Alt or Win, or that combination would stop you typing."
            : null;
    }

    /// <summary>Keys that don't type anything, so they work as a hotkey alone.</summary>
    private static bool IsStandaloneKey(int vk) =>
        vk is >= VirtualKeys.F1 and <= VirtualKeys.F24
            or VirtualKeys.Pause or VirtualKeys.ScrollLock or VirtualKeys.CapsLock;

    public override string ToString() => string.Join("+", Keys.Select(NameOf));

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var keys = new List<int>();
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParseKey(part, out var vk))
            {
                return false;
            }
            keys.Add(vk);
        }

        if (keys.Count == 0)
        {
            return false;
        }

        hotkey = keys.Count == 1 ? new Hotkey(keys) : FromPressedKeys(keys);
        return true;
    }

    public bool Equals(Hotkey? other) => other is not null && Keys.SequenceEqual(other.Keys);

    public override int GetHashCode() => Keys.Aggregate(17, (hash, key) => hash * 31 + key);

    public static string NameOf(int vk) => Names.TryGetValue(vk, out var name) ? name : $"Key{vk}";

    private static bool TryParseKey(string name, out int vk)
    {
        if (ByName.TryGetValue(name, out vk))
        {
            return true;
        }

        return name.StartsWith("Key", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(name.AsSpan(3), out vk)
               && vk is > 0 and < 256;
    }

    private static readonly Dictionary<int, string> Names = BuildNames();

    private static readonly Dictionary<string, int> ByName = Names
        .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [VirtualKeys.Ctrl] = "Ctrl",
            [VirtualKeys.Alt] = "Alt",
            [VirtualKeys.Shift] = "Shift",
            [VirtualKeys.Win] = "Win",
            [VirtualKeys.RightWin] = "Right Win",
            [VirtualKeys.LeftCtrl] = "Left Ctrl",
            [VirtualKeys.RightCtrl] = "Right Ctrl",
            [VirtualKeys.LeftAlt] = "Left Alt",
            [VirtualKeys.RightAlt] = "Right Alt",
            [VirtualKeys.LeftShift] = "Left Shift",
            [VirtualKeys.RightShift] = "Right Shift",
            [VirtualKeys.Backspace] = "Backspace",
            [VirtualKeys.Tab] = "Tab",
            [VirtualKeys.Enter] = "Enter",
            [VirtualKeys.Pause] = "Pause",
            [VirtualKeys.CapsLock] = "Caps Lock",
            [VirtualKeys.Escape] = "Esc",
            [VirtualKeys.Space] = "Space",
            [0x21] = "Page Up",
            [0x22] = "Page Down",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "Print Screen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x6A] = "Num *",
            [0x6B] = "Num Plus",
            [0x6D] = "Num -",
            [0x6E] = "Num .",
            [0x6F] = "Num /",
            [VirtualKeys.ScrollLock] = "Scroll Lock",
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = ",",
            [0xBD] = "-",
            [0xBE] = ".",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "'",
        };

        for (var c = 'A'; c <= 'Z'; c++)
        {
            names[c] = c.ToString();
        }

        for (var d = 0; d <= 9; d++)
        {
            names['0' + d] = d.ToString();
            names[0x60 + d] = $"Num {d}";
        }

        for (var f = 1; f <= 24; f++)
        {
            names[VirtualKeys.F1 + f - 1] = $"F{f}";
        }

        return names;
    }
}
