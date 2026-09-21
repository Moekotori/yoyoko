namespace Chat.UI.Shortcuts;

public enum ShortcutAction
{
    Jump,
    Search,
    PreviousChannel,
    NextChannel,
    PreviousTab,
    NextTab,
    CloseTab,
    Settings,
    Members,
    Attach,
    Mute,
    Deafen
}

public static class ShortcutIds
{
    public static string Id(this ShortcutAction action) => action switch
    {
        ShortcutAction.Jump => "jump",
        ShortcutAction.Search => "search",
        ShortcutAction.PreviousChannel => "previous_channel",
        ShortcutAction.NextChannel => "next_channel",
        ShortcutAction.PreviousTab => "previous_tab",
        ShortcutAction.NextTab => "next_tab",
        ShortcutAction.CloseTab => "close_tab",
        ShortcutAction.Settings => "settings",
        ShortcutAction.Members => "members",
        ShortcutAction.Attach => "attach",
        ShortcutAction.Mute => "mute",
        ShortcutAction.Deafen => "deafen",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static ShortcutAction? Parse(string? id) => id switch
    {
        "jump" => ShortcutAction.Jump,
        "search" => ShortcutAction.Search,
        "previous_channel" => ShortcutAction.PreviousChannel,
        "next_channel" => ShortcutAction.NextChannel,
        "previous_tab" => ShortcutAction.PreviousTab,
        "next_tab" => ShortcutAction.NextTab,
        "close_tab" => ShortcutAction.CloseTab,
        "settings" => ShortcutAction.Settings,
        "members" => ShortcutAction.Members,
        "attach" => ShortcutAction.Attach,
        "mute" => ShortcutAction.Mute,
        "deafen" => ShortcutAction.Deafen,
        _ => null
    };
}
