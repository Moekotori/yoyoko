using System.Collections.ObjectModel;
using Chat.UI.Channels;
using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private bool _switcherOpen;
    private string _switcherQuery = "";
    private JumpItem? _switcherSelected;
    public ObservableCollection<JumpItem> SwitcherMatches { get; } = [];
    public ActionCommand JumpTo { get; private set; } = null!;
    public Action? FocusSwitcher { get; set; }
    public Action? FocusSearch { get; set; }
    public bool SwitcherOpen
    {
        get => _switcherOpen;
        private set { if (_switcherOpen == value) return; _switcherOpen = value; Changed(); }
    }
    public string SwitcherQuery
    {
        get => _switcherQuery;
        set
        {
            if (_switcherQuery == value) return;
            _switcherQuery = value;
            Changed();
            if (SwitcherOpen) RefreshJump();
        }
    }
    public JumpItem? SwitcherSelected => _switcherSelected;
    public string AttachTip => FileLimitTip + " · ⌘ U / Ctrl U";
    public string MuteTip => MuteLabel + " · ⌘ ⇧ M / Ctrl Shift M";
    public string DeafTip => DeafLabel + " · ⌘ ⇧ D / Ctrl Shift D";

    private void InitializeKeyboard()
    {
        JumpTo = new(ConfirmJump);
    }

    public void OpenJump()
    {
        if (!IsSignedIn || IsChannelEditorOpen) return;
        if (ProfileOpen) ProfileOpen = false;
        if (SwitcherOpen)
        {
            FocusSwitcher?.Invoke();
            return;
        }
        if (SearchOpen) { SearchOpen = false; SearchQuery = ""; }
        _switcherQuery = "";
        Changed(nameof(SwitcherQuery));
        RefreshJump();
        SwitcherOpen = true;
        FocusSwitcher?.Invoke();
    }

    public void CloseJump()
    {
        if (!SwitcherOpen) return;
        SwitcherOpen = false;
        _switcherQuery = "";
        SwitcherMatches.Clear();
        _switcherSelected = null;
        Changed(nameof(SwitcherQuery));
        Changed(nameof(SwitcherSelected));
    }

    public void ConfirmJump(object? value)
    {
        var channel = value switch
        {
            JumpItem item => item.Channel,
            ChannelItem direct => direct,
            _ => _switcherSelected?.Channel
        };
        CloseJump();
        if (channel is null) return;
        SelectOpenChannel.Execute(channel);
        if (channel.Kind == "text") FocusComposer?.Invoke();
    }

    public void MoveJump(int delta)
    {
        if (SwitcherMatches.Count == 0) return;
        var index = _switcherSelected is null ? 0 : SwitcherMatches.IndexOf(_switcherSelected);
        if (index < 0) index = 0;
        var count = SwitcherMatches.Count;
        index = (index + delta) % count;
        if (index < 0) index += count;
        SetJumpActive(SwitcherMatches[index]);
    }

    public void OpenSearch()
    {
        if (IsChannelEditorOpen || SelectedChannel is not { Kind: "text" }) return;
        if (ProfileOpen) ProfileOpen = false;
        CloseJump();
        ShowSettings = false;
        SearchOpen = true;
        FocusSearch?.Invoke();
    }

    public void SelectAdjacentChannel(int delta)
    {
        if (!IsSignedIn || ShowSettings || IsChannelEditorOpen || SwitcherOpen) return;
        var channels = VisibleChannels();
        if (channels.Count == 0) return;
        var next = Adjacent(channels, SelectedChannel, delta);
        if (next is not null) SelectOpenChannel.Execute(next);
    }

    public void SelectAdjacentTab(int delta)
    {
        if (ShowSettings || IsChannelEditorOpen || SwitcherOpen || OpenChannels.Count == 0) return;
        var next = Adjacent(OpenChannels, SelectedChannel, delta);
        if (next is not null) SelectOpenChannel.Execute(next);
    }

    public void SelectOpenTab(int index)
    {
        if (ShowSettings || IsChannelEditorOpen || index < 0 || index >= OpenChannels.Count) return;
        SelectOpenChannel.Execute(OpenChannels[index]);
    }

    public void CloseCurrentTab()
    {
        if (ShowSettings || IsChannelEditorOpen || SwitcherOpen) return;
        if (SelectedChannel is { } channel) CloseOpenChannel.Execute(channel);
    }

    public void AppendDraft(string text)
    {
        if (!ShowChat || SwitcherOpen || IsChannelEditorOpen || string.IsNullOrEmpty(text) || text.All(char.IsControl)) return;
        Draft += text;
        FocusComposer?.Invoke();
    }

    private void RefreshJump()
    {
        var query = SwitcherQuery.Trim();
        var matches = TextChannels.Concat(VoiceChannels)
            .Where(channel => query.Length == 0 || channel.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        for (var i = SwitcherMatches.Count - 1; i >= 0; i--)
            if (!matches.Contains(SwitcherMatches[i].Channel)) SwitcherMatches.RemoveAt(i);
        for (var i = 0; i < matches.Count; i++)
        {
            if (i < SwitcherMatches.Count && SwitcherMatches[i].Channel == matches[i]) continue;
            var previous = -1;
            for (var j = 0; j < SwitcherMatches.Count; j++)
                if (SwitcherMatches[j].Channel == matches[i]) { previous = j; break; }
            if (previous >= 0) SwitcherMatches.Move(previous, i);
            else SwitcherMatches.Insert(i, new JumpItem(matches[i]));
        }
        var keep = _switcherSelected is not null
            ? SwitcherMatches.FirstOrDefault(item => item.Channel.Id == _switcherSelected.Channel.Id)
            : null;
        SetJumpActive(keep ?? SwitcherMatches.FirstOrDefault());
    }

    private void SetJumpActive(JumpItem? item)
    {
        foreach (var match in SwitcherMatches) match.IsActive = match == item;
        _switcherSelected = item;
        Changed(nameof(SwitcherSelected));
    }

    private IReadOnlyList<ChannelItem> VisibleChannels()
    {
        var list = new List<ChannelItem>();
        if (TextExpanded) list.AddRange(TextChannels);
        if (VoiceExpanded) list.AddRange(VoiceChannels);
        if (list.Count == 0) list.AddRange(TextChannels.Concat(VoiceChannels));
        return list;
    }

    private static ChannelItem? Adjacent(IReadOnlyList<ChannelItem> channels, ChannelItem? current, int delta)
    {
        if (channels.Count == 0) return null;
        var index = -1;
        if (current is not null)
        {
            for (var i = 0; i < channels.Count; i++)
                if (channels[i].Id == current.Id) { index = i; break; }
        }
        if (index < 0) return delta > 0 ? channels[0] : channels[^1];
        var next = (index + delta) % channels.Count;
        if (next < 0) next += channels.Count;
        return channels[next];
    }
}
