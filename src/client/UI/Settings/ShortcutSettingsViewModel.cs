using System.Collections.ObjectModel;
using System.ComponentModel;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Shortcuts;

namespace Chat.UI.Settings;

public sealed class ShortcutGroup : ObservableObject
{
    private string _title;
    public ShortcutGroup(string titleKey, string title, IReadOnlyList<ShortcutRow> rows)
    {
        TitleKey = titleKey;
        _title = title;
        Rows = rows;
    }
    public string TitleKey { get; }
    public IReadOnlyList<ShortcutRow> Rows { get; }
    public string Title { get => _title; internal set { if (_title == value) return; _title = value; Changed(); } }
}

public sealed class ShortcutRow : ObservableObject
{
    private string _title;
    private string _gesture;
    private IReadOnlyList<string> _tokens;
    private bool _recording;
    private bool _custom;
    public ShortcutRow(ShortcutAction action, string titleKey, string title, string gesture, IReadOnlyList<string> tokens, bool custom, bool last)
    {
        Action = action;
        TitleKey = titleKey;
        _title = title;
        _gesture = gesture;
        _tokens = tokens;
        _custom = custom;
        ShowDivider = !last;
    }
    public ShortcutAction Action { get; }
    public string TitleKey { get; }
    public bool ShowDivider { get; }
    public string Title { get => _title; private set { if (_title == value) return; _title = value; Changed(); } }
    public string Gesture { get => _gesture; private set { if (_gesture == value) return; _gesture = value; Changed(); } }
    public IReadOnlyList<string> Tokens { get => _tokens; private set { _tokens = value; Changed(); Changed(nameof(ShowKeys)); Changed(nameof(ShowUnbound)); } }
    public bool IsRecording
    {
        get => _recording;
        private set
        {
            if (_recording == value) return;
            _recording = value;
            Changed();
            Changed(nameof(ShowKeys));
            Changed(nameof(ShowUnbound));
        }
    }
    public bool IsCustom { get => _custom; private set { if (_custom == value) return; _custom = value; Changed(); } }
    public bool ShowKeys => !_recording && _tokens.Count > 0;
    public bool ShowUnbound => !_recording && _tokens.Count == 0;
    internal void Present(string title, string gesture, IReadOnlyList<string> tokens, bool custom, bool recording)
    {
        Title = title;
        Gesture = gesture;
        Tokens = tokens;
        IsCustom = custom;
        IsRecording = recording;
    }
}

public sealed class ShortcutSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IShortcutPreference _preference;
    private readonly IChatChrome _chrome;
    private readonly I18n _text;
    private ShortcutRow? _recording;
    public ShortcutSettingsViewModel(IShortcutPreference preference, IChatChrome chrome, I18n text)
    {
        _preference = preference;
        _chrome = chrome;
        _text = text;
        Capture = new(BeginCapture);
        ResetRow = new(ResetOne);
        ResetAll = new(_ => { CancelCapture(); _preference.Reset(); });
        _preference.Changed += Refresh;
        _chrome.Changed += OnChrome;
        _text.PropertyChanged += OnText;
        Rebuild();
    }

    public ObservableCollection<ShortcutGroup> Groups { get; } = [];
    public ActionCommand Capture { get; }
    public ActionCommand ResetRow { get; }
    public ActionCommand ResetAll { get; }
    public bool IsRecording => _recording is not null;
    public bool CanResetAll => _preference.Overrides.Count > 0;
    public IReadOnlyDictionary<string, string> Overrides => _preference.Overrides;
    public IReadOnlyList<string> SendShortcutChoices =>
    [
        _text.Get(TextKey.EnterToSend),
        _text.Get(TextKey.CtrlEnterToSend)
    ];
    public int SendMode
    {
        get => _chrome.EnterToSend ? 0 : 1;
        set { if (value is 0 or 1 && value != SendMode) _chrome.SetEnterToSend(value == 0); }
    }

    public bool HandleCapture(KeyChord? chord, bool backspace)
    {
        if (_recording is null) return false;
        if (backspace)
        {
            var action = _recording.Action;
            CancelCapture();
            _preference.Assign(action.Id(), "");
            return true;
        }
        if (chord is null) return true;
        if (chord.Value.Key is "escape") { CancelCapture(); return true; }
        var stored = chord.Value.ForStorage();
        if (!stored.IsValidAssignment()) return true;
        var actionId = _recording.Action;
        CancelCapture();
        foreach (var other in ShortcutScheme.Actions)
        {
            if (other == actionId) continue;
            var bound = ShortcutScheme.Resolve(other, _preference.Overrides);
            if (bound is { } existing && existing.Matches(stored))
                _preference.Assign(other.Id(), "");
        }
        _preference.Assign(actionId.Id(), stored.Serialize());
        return true;
    }

    public void CancelCapture()
    {
        if (_recording is null) return;
        var row = _recording;
        _recording = null;
        Changed(nameof(IsRecording));
        Apply(row);
    }

    public void Dispose()
    {
        _preference.Changed -= Refresh;
        _chrome.Changed -= OnChrome;
        _text.PropertyChanged -= OnText;
    }

    private void BeginCapture(object? value)
    {
        if (value is not ShortcutRow row) return;
        if (ReferenceEquals(_recording, row)) { CancelCapture(); return; }
        var previous = _recording;
        _recording = row;
        if (previous is not null) Apply(previous);
        row.Present(row.Title, _text.Get(TextKey.ShortcutPressKey), [], row.IsCustom, true);
        Changed(nameof(IsRecording));
    }

    private void ResetOne(object? value)
    {
        if (value is not ShortcutRow row) return;
        if (ReferenceEquals(_recording, row)) CancelCapture();
        _preference.Assign(row.Action.Id(), null);
    }

    private void OnText(object? sender, PropertyChangedEventArgs args) => Refresh();
    private void OnChrome()
    {
        Changed(nameof(SendMode));
        Changed(nameof(SendShortcutChoices));
    }

    private void Refresh()
    {
        if (Groups.Count == 0) Rebuild();
        else
        {
            foreach (var group in Groups)
            {
                group.Title = _text.Get(group.TitleKey);
                foreach (var row in group.Rows) Apply(row);
            }
        }
        Changed(nameof(CanResetAll));
        Changed(nameof(SendShortcutChoices));
    }

    private void Rebuild()
    {
        Groups.Clear();
        Groups.Add(Group(TextKey.ShortcutNavigation,
        [
            Item(ShortcutAction.Jump, TextKey.JumpToChannel),
            Item(ShortcutAction.PreviousChannel, TextKey.ShortcutPreviousChannel),
            Item(ShortcutAction.NextChannel, TextKey.ShortcutNextChannel),
            Item(ShortcutAction.PreviousTab, TextKey.ShortcutPreviousTab),
            Item(ShortcutAction.NextTab, TextKey.ShortcutNextTab),
            Item(ShortcutAction.CloseTab, TextKey.CloseTab),
            Item(ShortcutAction.Settings, TextKey.Settings, last: true)
        ]));
        Groups.Add(Group(TextKey.ChatBehavior,
        [
            Item(ShortcutAction.Search, TextKey.SearchMessages),
            Item(ShortcutAction.Members, TextKey.ToggleMembers),
            Item(ShortcutAction.Attach, TextKey.AddFile, last: true)
        ]));
        Groups.Add(Group(TextKey.Voice,
        [
            Item(ShortcutAction.Mute, TextKey.Mute),
            Item(ShortcutAction.Deafen, TextKey.Deafen, last: true)
        ]));
        Changed(nameof(CanResetAll));
    }

    private ShortcutGroup Group(string titleKey, IReadOnlyList<ShortcutRow> rows) =>
        new(titleKey, _text.Get(titleKey), rows);

    private ShortcutRow Item(ShortcutAction action, string key, bool last = false)
    {
        var recording = _recording?.Action == action;
        return new(action, key, _text.Get(key), recording ? _text.Get(TextKey.ShortcutPressKey) : Label(action),
            recording ? [] : ShortcutScheme.Tokens(action, _preference.Overrides),
            ShortcutScheme.Custom(action, _preference.Overrides), last);
    }

    private void Apply(ShortcutRow row)
    {
        var recording = ReferenceEquals(_recording, row);
        row.Present(_text.Get(row.TitleKey), recording ? _text.Get(TextKey.ShortcutPressKey) : Label(row.Action),
            recording ? [] : ShortcutScheme.Tokens(row.Action, _preference.Overrides),
            ShortcutScheme.Custom(row.Action, _preference.Overrides), recording);
    }

    private string Label(ShortcutAction action) =>
        ShortcutScheme.Display(action, _preference.Overrides, _text.Get(TextKey.ShortcutUnbound));
}
