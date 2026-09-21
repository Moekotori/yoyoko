using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using Chat.Core.Messaging;
using Chat.Localization;
using Chat.UI.Chat;
using Chat.UI.Components;
using ChannelItem = Chat.UI.Channels.ChannelItem;

namespace Chat.UI.Shell;

// Local presentation state; never supplies server membership or online presence.
public sealed partial class ShellViewModel
{
    private bool _searchOpen;
    private bool _participantsOpen = true;
    private bool _textExpanded = true;
    private bool _voiceExpanded = true;
    private bool _refreshQueued;
    private bool _channelsChanged;
    private bool _messagesChanged;
    private bool _presentationDisposed;
    private string _searchQuery = "";
    private string? _draftKey;
    private readonly Dictionary<string, string> _channelDrafts = [];
    public ObservableCollection<ChannelItem> OpenChannels { get; } = [];
    public ObservableCollection<MessageRow> VisibleMessages { get; } = [];
    public ObservableCollection<MemberProfile> Participants { get; } = [];
    public IEnumerable<ChannelItem> TextChannels => Channels.Where(channel => channel.Kind == "text");
    public IEnumerable<ChannelItem> VoiceChannels => Channels.Where(channel => channel.Kind == "voice");
    public ActionCommand SelectOpenChannel { get; private set; } = null!;
    public ActionCommand CloseOpenChannel { get; private set; } = null!;
    public ActionCommand ToggleSearch { get; private set; } = null!;
    public ActionCommand ToggleParticipants { get; private set; } = null!;
    public ActionCommand ToggleTextChannels { get; private set; } = null!;
    public ActionCommand ToggleVoiceChannels { get; private set; } = null!;
    public ActionCommand DismissPresentation { get; private set; } = null!;
    public bool SearchOpen { get => _searchOpen; private set { _searchOpen = value; Changed(); } }
    public bool ParticipantsOpen { get => _participantsOpen; private set { _participantsOpen = value; Changed(); } }
    public bool TextExpanded { get => _textExpanded; private set { _textExpanded = value; Changed(); } }
    public bool VoiceExpanded { get => _voiceExpanded; private set { _voiceExpanded = value; Changed(); } }
    public string SearchQuery { get => _searchQuery; set { _searchQuery = value; Changed(); RefreshMessagePresentation(); } }
    public string ComposerPlaceholder => OnCooldown
        ? _text.Get(TextKey.CooldownWait, CooldownLeft)
        : SelectedChannel is null ? _text.Get(TextKey.SelectChannel) : _text.Get(TextKey.MessageToChannel, SelectedChannel.Name);
    public string SendTip => OnCooldown
        ? ComposerPlaceholder
        : _text.Get(EnterToSend ? TextKey.EnterToSend : TextKey.CtrlEnterToSend);
    public string EmptyMessageTitle => SearchQuery.Length > 0 ? _text.Get(TextKey.NoMatchingMessages) : _text.Get(TextKey.StillQuiet);
    public bool EmptyMessages => VisibleMessages.Count == 0 && !IsChannelLoading;
    public int ParticipantCount => Participants.Count;
    public bool HasParticipants => Participants.Count > 0;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool ShowChannelEmpty => IsSignedIn && SelectedChannel is null && !ShowSettings;

    private void InitializePresentation()
    {
        InitializeChannelManagement();
        SelectOpenChannel = new(value => { if (value is ChannelItem channel) { ShowSettings = false; if (SelectedChannel?.Id != channel.Id) SelectedChannel = channel; } });
        CloseOpenChannel = new(value =>
        {
            if (value is not ChannelItem channel) return;
            var index = OpenChannels.IndexOf(channel);
            if (index < 0) return;
            OpenChannels.RemoveAt(index);
            if (SelectedChannel?.Id == channel.Id)
                SelectedChannel = OpenChannels.Count > 0 ? OpenChannels[Math.Min(index, OpenChannels.Count - 1)] : null;
        });
        ToggleSearch = new(_ =>
        {
            CloseJump();
            SearchOpen = !SearchOpen;
            if (!SearchOpen) SearchQuery = "";
            else FocusSearch?.Invoke();
        });
        ToggleParticipants = new(_ => ParticipantsOpen = !ParticipantsOpen);
        ToggleTextChannels = new(_ => { TextExpanded = !TextExpanded; if (!TextExpanded && ChannelEditor is { IsVoice: false } editor) editor.Cancel.Execute(null); });
        ToggleVoiceChannels = new(_ => { VoiceExpanded = !VoiceExpanded; if (!VoiceExpanded && ChannelEditor is { IsVoice: true } editor) editor.Cancel.Execute(null); });
        JumpPresent = new(_ => JumpToPresent());
        CancelComposerEdit = new(_ => CancelEdit());
        DismissPresentation = new(_ =>
        {
            if (IsEditing) CancelEdit();
            else if (ProfileOpen) ProfileOpen = false;
            else if (SwitcherOpen) CloseJump();
            else if (ChannelEditor is { } editor) editor.Cancel.Execute(null);
            else if (ShowSettings) ShowSettings = false;
            else if (SearchOpen) { SearchOpen = false; SearchQuery = ""; }
            else Status = "";
        });
        InitializeKeyboard();
        PropertyChanged += OnPresentationChanged;
        Channels.CollectionChanged += OnPresentationCollection;
        Messages.CollectionChanged += OnPresentationCollection;
    }

    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SelectedInstance))
        {
            ProfileOpen = false;
            ChannelEditor = null;
            _draftKey = null;
            Draft = "";
            OpenChannels.Clear();
            SelectedChannel = null;
            SearchQuery = "";
            CloseJump();
        }
        if (args.PropertyName == nameof(SelectedChannel))
        {
            FlushDraft();
            if (IsEditing) { _editingId = null; _editBackup = ""; Changed(nameof(IsEditing)); }
            _newWhileAway = 0;
            _awayFromBottom = false;
            foreach (var item in Channels.Concat(OpenChannels).Distinct()) item.IsSelected = item.Id == SelectedChannel?.Id;
            if (SelectedChannel is { } channel && !OpenChannels.Any(item => item.Id == channel.Id))
            {
                if (OpenChannels.Count >= 12) OpenChannels.RemoveAt(0);
                OpenChannels.Add(channel);
            }
            var account = SelectedInstance?.Context.Account;
            _draftKey = account is null || SelectedChannel is null ? null
                : $"{SelectedInstance!.Context.Descriptor.Id.Value}:{account.Key.Id}:{SelectedChannel.Id}";
            Draft = _draftKey is not null && _channelDrafts.TryGetValue(_draftKey, out var draft) ? draft : "";
            Changed(nameof(ComposerPlaceholder));
            Changed(nameof(SendTip));
            Changed(nameof(ShowChannelEmpty));
            Changed(nameof(ShowJumpBar));
            Changed(nameof(ChannelHasUnread));
            Changed(nameof(InSelectedVoice));
            if (SearchOpen) FocusSearch?.Invoke();
            else if (!SwitcherOpen && SelectedChannel is { Kind: "text" }) FocusComposer?.Invoke();
        }
        if (args.PropertyName == nameof(Draft) && _draftKey is not null && !IsEditing)
        {
            if (string.IsNullOrEmpty(Draft)) _channelDrafts.Remove(_draftKey);
            else
            {
                if (_channelDrafts.Count >= 32 && !_channelDrafts.ContainsKey(_draftKey)) _channelDrafts.Remove(_channelDrafts.Keys.First());
                _channelDrafts[_draftKey] = Draft;
            }
            QueueDraftSave();
        }
        if (args.PropertyName is nameof(IsSignedIn) or nameof(ShowSettings)) Changed(nameof(ShowChannelEmpty));
        if (args.PropertyName == nameof(IsSignedIn) && !IsSignedIn)
        {
            _draftKey = null;
            OpenChannels.Clear();
            CloseJump();
        }
        if ((args.PropertyName == nameof(IsSignedIn) && !IsSignedIn)
            || (args.PropertyName == nameof(ShowSettings) && ShowSettings)) ProfileOpen = false;
        if (args.PropertyName == nameof(Status)) Changed(nameof(HasStatus));
    }

    private void OnPresentationCollection(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (ReferenceEquals(sender, Channels)) _channelsChanged = true;
        else _messagesChanged = true;
        if (_refreshQueued || _presentationDisposed) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (_presentationDisposed) return;
            if (_channelsChanged)
            {
                _channelsChanged = false;
                RefreshChannelEditor();
            Changed(nameof(TextChannels)); Changed(nameof(VoiceChannels));
                for (var index = OpenChannels.Count - 1; index >= 0; index--)
                {
                    var current = Channels.FirstOrDefault(item => item.Id == OpenChannels[index].Id);
                    if (current is null) OpenChannels.RemoveAt(index);
                    else if (!ReferenceEquals(current, OpenChannels[index])) OpenChannels[index] = current;
                }
                foreach (var item in Channels.Concat(OpenChannels).Distinct()) item.IsSelected = item.Id == SelectedChannel?.Id;
                if (SwitcherOpen) RefreshJump();
            }
            if (_messagesChanged) RefreshMessagePresentation();
        });
    }

    private void RefreshMessagePresentation()
    {
        _messagesChanged = false;
        var matches = Messages.Where(row => string.IsNullOrWhiteSpace(SearchQuery)
            || row.Content.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            || row.Author.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)).ToList();
        // Preserve unchanged containers so incoming messages don't reset selection or scroll.
        for (var i = VisibleMessages.Count - 1; i >= 0; i--)
            if (!matches.Contains(VisibleMessages[i])) VisibleMessages.RemoveAt(i);
        for (var i = 0; i < matches.Count; i++)
            if (i >= VisibleMessages.Count || VisibleMessages[i] != matches[i])
            {
                var previousIndex = VisibleMessages.IndexOf(matches[i]);
                if (previousIndex >= 0) VisibleMessages.Move(previousIndex, i); else VisibleMessages.Insert(i, matches[i]);
            }
        RefreshParticipants();
        InsertUnreadDivider(matches);
        MessageRow? previous = null;
        for (var i = 0; i < VisibleMessages.Count; i++)
        {
            var row = VisibleMessages[i];
            if (row.IsUnreadDivider) { previous = null; continue; }
            row.SetContinuation(previous is not null && Continues(previous, row));
            previous = row;
        }
        Changed(nameof(EmptyMessages)); Changed(nameof(EmptyMessageTitle));
        Changed(nameof(ChannelHasUnread));
        Changed(nameof(ShowJumpBar));
    }

    private void InsertUnreadDivider(List<MessageRow> matches)
    {
        if (SearchQuery.Length > 0 || SelectedChannel is not { Kind: "text" } channel) return;
        if (!_inbox.TryGetValue(channel.Id, out var inbox) || inbox.LastReadId is not Guid read) return;
        var firstUnread = -1;
        for (var i = 0; i < matches.Count; i++)
            if (!matches[i].IsPending && MessageMarkup.IdAfter(matches[i].Id, read))
            {
                firstUnread = i;
                break;
            }
        if (firstUnread < 0) return;
        VisibleMessages.Insert(firstUnread, _unreadDivider);
    }

    private void RefreshParticipants()
    {
        var session = SelectedInstance?.Context.Session;
        var me = session?.Me.Id;
        var next = Messages
            .DistinctBy(row => row.Item.Message.AuthorId)
            .OrderBy(row => row.Author, StringComparer.CurrentCultureIgnoreCase)
            .Take(50)
            .Select(row =>
            {
                var id = row.Item.Message.AuthorId;
                var user = session?.User(id);
                var name = user?.DisplayName ?? row.Author;
                var username = user?.Username ?? "";
                var isSelf = me is Guid self && self == id;
                var existing = Participants.FirstOrDefault(item => item.Id == id);
                if (existing is null) existing = new MemberProfile(id, name, username, isSelf);
                else existing.Update(name, username, isSelf);
                existing.Playback = _playbacks.TryGetValue(id, out var cached) ? cached.Playback : row.Playback;
                return existing;
            })
            .ToList();
        for (var i = Participants.Count - 1; i >= 0; i--)
            if (next.All(item => item.Id != Participants[i].Id))
                Participants.RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            if (i < Participants.Count && Participants[i].Id == next[i].Id) continue;
            var previous = -1;
            for (var j = 0; j < Participants.Count; j++)
                if (Participants[j].Id == next[i].Id) { previous = j; break; }
            if (previous >= 0) Participants.Move(previous, i);
            else Participants.Insert(i, next[i]);
        }
        Changed(nameof(ParticipantCount));
        Changed(nameof(HasParticipants));
    }

    public void MentionMember(MemberProfile member)
    {
        var token = member.MentionToken;
        if (string.IsNullOrEmpty(token)) return;
        if (Draft.Length > 0 && !char.IsWhiteSpace(Draft[^1]))
            AppendDraft(" ");
        AppendDraft(token + " ");
    }

    private static bool Continues(MessageRow previous, MessageRow current)
    {
        if (previous.Item.Message.AuthorId != current.Item.Message.AuthorId) return false;
        if (previous.IsFailed || current.IsFailed) return false;
        var gap = current.Item.Message.CreatedAt - previous.Item.Message.CreatedAt;
        return gap >= TimeSpan.Zero && gap < TimeSpan.FromMinutes(5);
    }

    private void ClearAccountDrafts()
    {
        var account = SelectedInstance?.Context.Account;
        if (account is null) return;
        var prefix = $"{SelectedInstance!.Context.Descriptor.Id.Value}:{account.Key.Id}:";
        foreach (var key in _channelDrafts.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _channelDrafts.Remove(key);
    }

    private void DisposePresentation()
    {
        _presentationDisposed = true;
        if (_draftTimer is not null) _draftTimer.Tick -= OnDraftTick;
        _draftTimer?.Stop();
        PropertyChanged -= OnPresentationChanged;
        Channels.CollectionChanged -= OnPresentationCollection;
        Messages.CollectionChanged -= OnPresentationCollection;
        CloseJump();
        _channelDrafts.Clear();
        _inbox.Clear();
    }
}
