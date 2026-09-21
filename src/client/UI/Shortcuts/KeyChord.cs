using Avalonia.Input;
using InputKey = Avalonia.Input.Key;

namespace Chat.UI.Shortcuts;

[Flags]
public enum ChordModifier
{
    None = 0,
    Primary = 1,
    Control = 2,
    Meta = 4,
    Alt = 8,
    Shift = 16
}

public readonly record struct KeyChord(ChordModifier Modifiers, string Key)
{
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var key = parts[^1].ToLowerInvariant();
        if (key is "ctrl" or "control" or "cmd" or "command" or "meta" or "alt" or "option" or "shift" or "mod" or "primary")
            return null;
        var modifiers = ChordModifier.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var flag = parts[i].ToLowerInvariant() switch
            {
                "mod" or "primary" => ChordModifier.Primary,
                "ctrl" or "control" => ChordModifier.Control,
                "cmd" or "command" or "meta" => ChordModifier.Meta,
                "alt" or "option" => ChordModifier.Alt,
                "shift" => ChordModifier.Shift,
                _ => ChordModifier.None
            };
            if (flag == ChordModifier.None) return null;
            modifiers |= flag;
        }
        return new(modifiers, key);
    }

    public string Serialize()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ChordModifier.Primary)) parts.Add("mod");
        if (Modifiers.HasFlag(ChordModifier.Control)) parts.Add("ctrl");
        if (Modifiers.HasFlag(ChordModifier.Alt)) parts.Add("alt");
        if (Modifiers.HasFlag(ChordModifier.Shift)) parts.Add("shift");
        if (Modifiers.HasFlag(ChordModifier.Meta)) parts.Add("meta");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    public IReadOnlyList<string> TokenLabels()
    {
        var mac = OperatingSystem.IsMacOS();
        var parts = new List<string>(5);
        if (Has(ChordModifier.Primary)) parts.Add(mac ? "⌘" : "Ctrl");
        if (Has(ChordModifier.Control)) parts.Add("Ctrl");
        if (Has(ChordModifier.Meta)) parts.Add(mac ? "⌘" : "Win");
        if (Has(ChordModifier.Alt)) parts.Add(mac ? "⌥" : "Alt");
        if (Has(ChordModifier.Shift)) parts.Add(mac ? "⇧" : "Shift");
        parts.Add(KeyLabel());
        return parts;
    }

    public string Display() => string.Join(' ', TokenLabels());

    public bool Matches(KeyChord pressed)
    {
        if (!string.Equals(Key, pressed.Key, StringComparison.OrdinalIgnoreCase)) return false;
        return Expand() == pressed.Expand();
    }

    public KeyChord ForStorage()
    {
        var primary = OperatingSystem.IsMacOS() ? ChordModifier.Meta : ChordModifier.Control;
        var other = OperatingSystem.IsMacOS() ? ChordModifier.Control : ChordModifier.Meta;
        if (Has(primary) && !Has(other))
            return this with { Modifiers = (Modifiers & ~primary) | ChordModifier.Primary };
        return this;
    }

    public bool IsValidAssignment()
    {
        if (Key is "escape" or "enter" or "backspace" or "delete") return false;
        if (Key.Length >= 2 && Key[0] == 'f' && Key.Skip(1).All(char.IsDigit)) return true;
        return Has(ChordModifier.Primary | ChordModifier.Control | ChordModifier.Meta | ChordModifier.Alt);
    }

    public static KeyChord? FromKeyEvent(KeyEventArgs e)
    {
        if (e.Key is InputKey.None or InputKey.ImeProcessed or InputKey.DeadCharProcessed || IsModifier(e.Key)) return null;
        var key = Token(e.Key);
        if (key is null) return null;
        var modifiers = ChordModifier.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= ChordModifier.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= ChordModifier.Meta;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= ChordModifier.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= ChordModifier.Shift;
        return new(modifiers, key);
    }

    private ChordModifier Expand()
    {
        var modifiers = Modifiers;
        if (!modifiers.HasFlag(ChordModifier.Primary)) return modifiers;
        modifiers &= ~ChordModifier.Primary;
        modifiers |= OperatingSystem.IsMacOS() ? ChordModifier.Meta : ChordModifier.Control;
        return modifiers;
    }

    private bool Has(ChordModifier flag) => (Modifiers & flag) != 0;

    private string KeyLabel() => Key switch
    {
        "comma" => ",",
        "period" => ".",
        "slash" => "/",
        "backslash" => "\\",
        "minus" => "-",
        "equal" => "=",
        "semicolon" => ";",
        "quote" => "'",
        "bracketleft" => "[",
        "bracketright" => "]",
        "grave" => "`",
        "up" => "↑",
        "down" => "↓",
        "left" => "←",
        "right" => "→",
        "tab" => "Tab",
        "space" => "Space",
        "pageup" => "Page Up",
        "pagedown" => "Page Down",
        "home" => "Home",
        "end" => "End",
        "insert" => "Insert",
        "delete" => "Delete",
        "backspace" => "Backspace",
        "enter" => "Enter",
        var key when key.Length == 1 => key.ToUpperInvariant(),
        var key when key.StartsWith('f') && key.Length > 1 && key.Skip(1).All(char.IsDigit) => key.ToUpperInvariant(),
        _ => char.ToUpperInvariant(Key[0]) + Key[1..]
    };

    private static bool IsModifier(InputKey key) => key is
        InputKey.LeftCtrl or InputKey.RightCtrl or InputKey.LeftAlt or InputKey.RightAlt or
        InputKey.LeftShift or InputKey.RightShift or InputKey.LWin or InputKey.RWin;

    private static string? Token(InputKey key) => key switch
    {
        >= InputKey.A and <= InputKey.Z => ((char)('a' + (key - InputKey.A))).ToString(),
        >= InputKey.D0 and <= InputKey.D9 => ((char)('0' + (key - InputKey.D0))).ToString(),
        >= InputKey.NumPad0 and <= InputKey.NumPad9 => ((char)('0' + (key - InputKey.NumPad0))).ToString(),
        >= InputKey.F1 and <= InputKey.F12 => "f" + (key - InputKey.F1 + 1),
        InputKey.OemComma => "comma",
        InputKey.OemPeriod => "period",
        InputKey.OemMinus => "minus",
        InputKey.OemPlus => "equal",
        InputKey.Oem2 => "slash",
        InputKey.Oem5 => "backslash",
        InputKey.Oem1 => "semicolon",
        InputKey.Oem7 => "quote",
        InputKey.Oem4 => "bracketleft",
        InputKey.Oem6 => "bracketright",
        InputKey.Oem3 => "grave",
        InputKey.Up => "up",
        InputKey.Down => "down",
        InputKey.Left => "left",
        InputKey.Right => "right",
        InputKey.Tab => "tab",
        InputKey.Space => "space",
        InputKey.PageUp => "pageup",
        InputKey.PageDown => "pagedown",
        InputKey.Home => "home",
        InputKey.End => "end",
        InputKey.Insert => "insert",
        InputKey.Delete => "delete",
        InputKey.Back => "backspace",
        InputKey.Enter => "enter",
        InputKey.Escape => "escape",
        _ => null
    };
}
