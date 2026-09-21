using System.Collections.ObjectModel;
using Chat.Core.Messaging;
using Chat.Localization;
using Chat.UI.Channels;
using Chat.UI.Chat;
using Chat.UI.Components;
using Chat.UI.Shortcuts;

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
    public string AttachTip => FileLimitTip + " · " + ChordLabel(ShortcutAction.Attach);
    public string MuteTip => MuteLabel + " · " + ChordLabel(ShortcutAction.Mute);
    public string DeafTip => DeafLabel + " · " + ChordLabel(ShortcutAction.Deafen);
    public string SearchShortcutTip => ChordTip(TextKey.SearchMessages, ShortcutAction.Search);
    public string MembersShortcutTip => ChordTip(TextKey.Members, ShortcutAction.Members);
    public string SettingsShortcutTip => ChordTip(ShowSettings ? TextKey.CloseSettings : TextKey.Settings, ShortcutAction.Settings);
    public string CloseTabShortcutTip => ChordTip(TextKey.CloseTab, ShortcutAction.CloseTab);

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
        var picked = value as JumpItem ?? _switcherSelected;
        if (picked?.IsSection == true) return;
        CloseJump();
        if (picked?.Person is { } person)
        {
            MentionMember(person);
            FocusComposer?.Invoke();
            return;
        }
        var channel = picked?.Channel ?? value as ChannelItem;
        if (channel is null) return;
        if (channel.Kind == "voice")
        {
            JoinVoice.Execute(channel);
            return;
        }
        SelectOpenChannel.Execute(channel);
        FocusComposer?.Invoke();
    }

    public void MoveJump(int delta)
    {
        if (SwitcherMatches.Count == 0) return;
        var index = _switcherSelected is null ? 0 : SwitcherMatches.IndexOf(_switcherSelected);
        if (index < 0) index = 0;
        var count = SwitcherMatches.Count;
        for (var step = 0; step < count; step++)
        {
            index = (index + delta) % count;
            if (index < 0) index += count;
            if (!SwitcherMatches[index].IsSection) break;
        }
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
        var all = TextChannels.Concat(VoiceChannels).ToList();
        var visited = new Dictionary<Guid, long>();
        foreach (var row in _inbox.Values)
            if (row.VisitedAt is long at) visited[row.ChannelId] = at;
        var recentIds = all.Where(channel => visited.ContainsKey(channel.Id))
            .OrderByDescending(channel => visited[channel.Id])
            .Take(JumpRank.RecentCap)
            .Select(channel => channel.Id)
            .ToHashSet();
        var ranked = JumpRank.Channels(all, channel => channel.Id, channel => channel.Name, visited, query);
        var next = new List<JumpItem>();
        if (query.Length == 0 && recentIds.Count > 0)
            next.Add(JumpItem.Header(_text.Get(TextKey.Recent)));
        next.AddRange(ranked.Select(channel => new JumpItem(channel, recentIds.Contains(channel.Id))));
        if (query.Length > 0)
        {
            var people = PeopleMatches(query).ToList();
            if (people.Count > 0)
            {
                next.Add(JumpItem.Header(_text.Get(TextKey.Members)));
                next.AddRange(people.Select(person => new JumpItem(person)));
            }
        }
        for (var i = SwitcherMatches.Count - 1; i >= 0; i--)
            if (next.All(item => !SameJump(item, SwitcherMatches[i]))) SwitcherMatches.RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            if (i < SwitcherMatches.Count && SameJump(SwitcherMatches[i], next[i])) continue;
            var previous = -1;
            for (var j = 0; j < SwitcherMatches.Count; j++)
                if (SameJump(SwitcherMatches[j], next[i])) { previous = j; break; }
            if (previous >= 0) SwitcherMatches.Move(previous, i);
            else SwitcherMatches.Insert(i, next[i]);
        }
        var keep = _switcherSelected is not null
            ? SwitcherMatches.FirstOrDefault(item => SameJump(item, _switcherSelected))
            : null;
        SetJumpActive(keep ?? SwitcherMatches.FirstOrDefault(item => !item.IsSection));
    }

    private IEnumerable<MemberProfile> PeopleMatches(string query)
    {
        var seen = new HashSet<Guid>();
        foreach (var person in Participants.Concat(VoicePeople()))
        {
            if (!seen.Add(person.Id)) continue;
            if (person.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || person.Username.Contains(query, StringComparison.OrdinalIgnoreCase))
                yield return person;
        }
    }

    private IEnumerable<MemberProfile> VoicePeople()
    {
        foreach (var channel in Channels)
            foreach (var member in channel.VoiceMembers)
                if (member.Profile is not null) yield return member.Profile;
    }

    private static bool SameJump(JumpItem left, JumpItem right) =>
        left.IsSection == right.IsSection
        && left.SectionTitle == right.SectionTitle
        && left.Person?.Id == right.Person?.Id
        && left.Channel?.Id == right.Channel?.Id;

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
        if (list.Count == 0) list.AddRange(TextChannels);
        return list;
    }

    public bool ExecuteShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.Jump:
                OpenJump();
                return true;
            case ShortcutAction.Search:
                OpenSearch();
                return true;
            case ShortcutAction.PreviousChannel:
                SelectAdjacentChannel(-1);
                return true;
            case ShortcutAction.NextChannel:
                SelectAdjacentChannel(1);
                return true;
            case ShortcutAction.PreviousTab:
                SelectAdjacentTab(-1);
                return true;
            case ShortcutAction.NextTab:
                SelectAdjacentTab(1);
                return true;
            case ShortcutAction.CloseTab:
                CloseCurrentTab();
                return true;
            case ShortcutAction.Settings:
                OpenSettings.Execute(null);
                return true;
            case ShortcutAction.Members:
                ToggleParticipants.Execute(null);
                return true;
            case ShortcutAction.Attach:
                if (ShowChat && AttachFile.CanExecute(null)) AttachFile.Execute(null);
                return true;
            case ShortcutAction.Mute:
                if (IsSignedIn && ToggleMute.CanExecute(null)) ToggleMute.Execute(null);
                return true;
            case ShortcutAction.Deafen:
                if (IsSignedIn && ToggleDeaf.CanExecute(null)) ToggleDeaf.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private void OnShortcutsChanged()
    {
        Changed(nameof(AttachTip));
        Changed(nameof(MuteTip));
        Changed(nameof(DeafTip));
        Changed(nameof(SearchShortcutTip));
        Changed(nameof(MembersShortcutTip));
        Changed(nameof(SettingsShortcutTip));
        Changed(nameof(CloseTabShortcutTip));
    }

    private string ChordTip(string labelKey, ShortcutAction action) =>
        _text.Get(labelKey) + " · " + ChordLabel(action);

    private string ChordLabel(ShortcutAction action) =>
        ShortcutScheme.Display(action, _shortcuts.Overrides, _text.Get(TextKey.ShortcutUnbound));

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
