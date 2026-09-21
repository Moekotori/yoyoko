using System.Collections.Frozen;

namespace Chat.UI.Shortcuts;

public static class ShortcutScheme
{
    public static IReadOnlyList<ShortcutAction> Actions { get; } =
    [
        ShortcutAction.Jump, ShortcutAction.Search, ShortcutAction.PreviousChannel, ShortcutAction.NextChannel,
        ShortcutAction.PreviousTab, ShortcutAction.NextTab, ShortcutAction.CloseTab, ShortcutAction.Settings,
        ShortcutAction.Members, ShortcutAction.Attach, ShortcutAction.Mute, ShortcutAction.Deafen
    ];

    private static readonly FrozenDictionary<ShortcutAction, KeyChord> Defaults = new Dictionary<ShortcutAction, KeyChord>
    {
        [ShortcutAction.Jump] = new(ChordModifier.Primary, "k"),
        [ShortcutAction.Search] = new(ChordModifier.Primary, "f"),
        [ShortcutAction.PreviousChannel] = new(ChordModifier.Alt, "up"),
        [ShortcutAction.NextChannel] = new(ChordModifier.Alt, "down"),
        [ShortcutAction.PreviousTab] = new(ChordModifier.Control | ChordModifier.Shift, "tab"),
        [ShortcutAction.NextTab] = new(ChordModifier.Control, "tab"),
        [ShortcutAction.CloseTab] = new(ChordModifier.Primary, "w"),
        [ShortcutAction.Settings] = new(ChordModifier.Primary, "comma"),
        [ShortcutAction.Members] = new(ChordModifier.Primary | ChordModifier.Shift, "u"),
        [ShortcutAction.Attach] = new(ChordModifier.Primary, "u"),
        [ShortcutAction.Mute] = new(ChordModifier.Primary | ChordModifier.Shift, "m"),
        [ShortcutAction.Deafen] = new(ChordModifier.Primary | ChordModifier.Shift, "d")
    }.ToFrozenDictionary();

    public static KeyChord? Resolve(ShortcutAction action, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.TryGetValue(action.Id(), out var raw))
        {
            if (raw.Length == 0) return null;
            if (KeyChord.Parse(raw) is { } chord) return chord;
        }
        return Defaults[action];
    }

    public static ShortcutAction? Match(KeyChord pressed, IReadOnlyDictionary<string, string> overrides)
    {
        foreach (var action in Actions)
        {
            var bound = Resolve(action, overrides);
            if (bound is { } chord && chord.Matches(pressed)) return action;
        }
        return null;
    }

    public static string Display(ShortcutAction action, IReadOnlyDictionary<string, string> overrides, string unbound)
        => Resolve(action, overrides)?.Display() ?? unbound;

    public static IReadOnlyList<string> Tokens(ShortcutAction action, IReadOnlyDictionary<string, string> overrides)
        => Resolve(action, overrides)?.TokenLabels() ?? [];

    public static bool Custom(ShortcutAction action, IReadOnlyDictionary<string, string> overrides)
        => overrides.ContainsKey(action.Id());
}
