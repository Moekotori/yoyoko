using System.Collections.ObjectModel;
using System.ComponentModel;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Shortcuts;

namespace Chat.UI.Settings;

public sealed class ShortcutGroup(string title, IReadOnlyList<ShortcutRow> rows)
{
    public string Title { get; internal set; } = title;
    public IReadOnlyList<ShortcutRow> Rows { get; } = rows;
}

public sealed class ShortcutRow(ShortcutAction action, string title, string gesture, bool last = false) : ObservableObject
{
    private string _title = title;
    private string _gesture = gesture;
    private bool _recording;
    public ShortcutAction Action { get; } = action;
    public bool ShowDivider { get; } = !last;
    public string Title { get => _title; internal set { if (_title == value) return; _title = value; Changed(); } }
    public string Gesture { get => _gesture; internal set { if (_gesture == value) return; _gesture = value; Changed(); } }
    public bool IsRecording { get => _recording; internal set { if (_recording == value) return; _recording = value; Changed(); } }
}

public sealed class ShortcutSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IShortcutPreference _preference;
    private readonly I18n _text;
    private ShortcutRow? _recording;
    public ShortcutSettingsViewModel(IShortcutPreference preference, I18n text)
    {
        _preference = preference;
        _text = text;
        Capture = new(BeginCapture);
        ResetAll = new(_ => { CancelCapture(); _preference.Reset(); });
        _preference.Changed += Refresh;
        _text.PropertyChanged += OnText;
        Rebuild();
    }

    public ObservableCollection<ShortcutGroup> Groups { get; } = [];
    public ActionCommand Capture { get; }
    public ActionCommand ResetAll { get; }
    public bool IsRecording => _recording is not null;
    public IReadOnlyDictionary<string, string> Overrides => _preference.Overrides;

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
        _recording.IsRecording = false;
        _recording = null;
        Changed(nameof(IsRecording));
        Refresh();
    }

    public void Dispose()
    {
        _preference.Changed -= Refresh;
        _text.PropertyChanged -= OnText;
    }

    private void BeginCapture(object? value)
    {
        if (value is not ShortcutRow row) return;
        if (ReferenceEquals(_recording, row)) { CancelCapture(); return; }
        if (_recording is not null) _recording.IsRecording = false;
        _recording = row;
        row.IsRecording = true;
        row.Gesture = _text.Get(TextKey.ShortcutPressKey);
        Changed(nameof(IsRecording));
    }

    private void OnText(object? sender, PropertyChangedEventArgs args) => Refresh();

    private void Refresh()
    {
        if (_recording is not null)
        {
            foreach (var group in Groups)
                foreach (var row in group.Rows)
                    if (!row.IsRecording) row.Gesture = Gesture(row.Action);
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        Groups.Clear();
        Groups.Add(new(_text.Get(TextKey.ShortcutNavigation),
        [
            Row(ShortcutAction.Jump, TextKey.JumpToChannel),
            Row(ShortcutAction.PreviousChannel, TextKey.ShortcutPreviousChannel),
            Row(ShortcutAction.NextChannel, TextKey.ShortcutNextChannel),
            Row(ShortcutAction.PreviousTab, TextKey.ShortcutPreviousTab),
            Row(ShortcutAction.NextTab, TextKey.ShortcutNextTab),
            Row(ShortcutAction.CloseTab, TextKey.CloseTab),
            Row(ShortcutAction.Settings, TextKey.Settings, last: true)
        ]));
        Groups.Add(new(_text.Get(TextKey.ChatBehavior),
        [
            Row(ShortcutAction.Search, TextKey.SearchMessages),
            Row(ShortcutAction.Members, TextKey.ToggleMembers),
            Row(ShortcutAction.Attach, TextKey.AddFile, last: true)
        ]));
        Groups.Add(new(_text.Get(TextKey.Voice),
        [
            Row(ShortcutAction.Mute, TextKey.Mute),
            Row(ShortcutAction.Deafen, TextKey.Deafen, last: true)
        ]));
    }

    private ShortcutRow Row(ShortcutAction action, string key, bool last = false) =>
        new(action, _text.Get(key), Gesture(action), last);

    private string Gesture(ShortcutAction action) =>
        ShortcutScheme.Display(action, _preference.Overrides, _text.Get(TextKey.ShortcutUnbound));
}
