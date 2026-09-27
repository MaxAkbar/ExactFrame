namespace ExactFrame.Core.Models;

/// <summary>Modifier flags. Values match the Win32 <c>MOD_*</c> constants used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>A global shortcut: modifiers plus a Win32 virtual-key code.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int VkF8 = 0x77;
    public const int VkF9 = 0x78;
    public const int VkF10 = 0x79;

    /// <summary>Requires a key and at least Ctrl, Alt or Win, so plain typing is never captured.</summary>
    public bool IsValid =>
        VirtualKey != 0 &&
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) != 0;

    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) keys.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) keys.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) keys.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) keys.Add("Win");
            if (VirtualKey != 0) keys.Add(KeyName(VirtualKey));
            return keys;
        }
    }

    public override string ToString() => VirtualKey == 0 ? "Not set" : string.Join(" + ", Keys);

    public static HotkeyGesture CtrlAlt(int virtualKey) =>
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, virtualKey);

    public static bool IsModifierKey(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    public static string KeyName(int virtualKey) => virtualKey switch
    {
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x60 and <= 0x69 => $"Num {virtualKey - 0x60}",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => $"Key {virtualKey:X2}"
    };
}
