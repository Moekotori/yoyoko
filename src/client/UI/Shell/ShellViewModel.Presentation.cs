using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using Chat.Core.Messaging;
using Chat.Localization;
using Chat.UI.Chat;
using Chat.UI.Components;
using ChannelItem = Chat.UI.Channels.ChannelItem;
using VoiceMemberRow = Chat.UI.Channels.VoiceMemberRow;

namespace Chat.UI.Shell;

// Local presentation state. Presence is not a server feature; extra names are layout fixtures.
public sealed partial class ShellViewModel
{
    private bool _searchOpen;
    private bool _participantsOpen = true;
    private bool _textExpanded = true;
    private bool _voiceExpanded = true;
    private bool _directExpanded = true;
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
    public IReadOnlyList<MemberSection> ParticipantSections { get; private set; } = [];
    public IEnumerable<ChannelItem> TextChannels => Channels.Where(channel => channel.Kind == "text");
    public IEnumerable<ChannelItem> VoiceChannels => Channels.Where(channel => channel.Kind == "voice");
    public IEnumerable<ChannelItem> DirectChannels => Channels.Where(channel => channel.IsDirect);
    public bool HasDirectChannels => Channels.Any(channel => channel.IsDirect);
    public ActionCommand SelectOpenChannel { get; private set; } = null!;
    public ActionCommand CloseOpenChannel { get; private set; } = null!;
    public ActionCommand ToggleSearch { get; private set; } = null!;
    public ActionCommand ToggleParticipants { get; private set; } = null!;
    public ActionCommand ToggleTextChannels { get; private set; } = null!;
    public ActionCommand ToggleVoiceChannels { get; private set; } = null!;
    public ActionCommand ToggleDirectMessages { get; private set; } = null!;
    public ActionCommand DismissPresentation { get; private set; } = null!;
    public bool SearchOpen { get => _searchOpen; private set { _searchOpen = value; Changed(); } }
    public bool ParticipantsOpen { get => _participantsOpen; private set { _participantsOpen = value; Changed(); } }
    public bool TextExpanded { get => _textExpanded; private set { _textExpanded = value; Changed(); } }
    public bool VoiceExpanded { get => _voiceExpanded; private set { _voiceExpanded = value; Changed(); } }
    public bool DirectExpanded { get => _directExpanded; private set { _directExpanded = value; Changed(); } }
    public string SearchQuery { get => _searchQuery; set { _searchQuery = value; Changed(); RefreshMessagePresentation(); } }
    public string ComposerPlaceholder => OnCooldown
        ? _text.Get(TextKey.CooldownWait, CooldownLeft)
        : SelectedChannel is null ? _text.Get(TextKey.SelectChannel)
        : SelectedChannel.IsDirect ? _text.Get(TextKey.MessageToDirect, SelectedChannel.Name)
        : _text.Get(TextKey.MessageToChannel, SelectedChannel.Name);
    public string SendTip => OnCooldown
        ? ComposerPlaceholder
        : _text.Get(EnterToSend ? TextKey.EnterToSend : TextKey.CtrlEnterToSend);
    public string EmptyMessageTitle => ChannelForbidden
        ? _text.Get(TextKey.ChannelForbidden)
        : SearchQuery.Length > 0 ? _text.Get(TextKey.NoMatchingMessages) : _text.Get(TextKey.StillQuiet);
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
        ToggleDirectMessages = new(_ => DirectExpanded = !DirectExpanded);
        JumpPresent = new(_ => JumpToPresent());
        CancelComposerEdit = new(_ => CancelComposer());
        DismissPresentation = new(_ =>
        {
            if (MentionOpen) CloseMention();
            else if (IsEditing) CancelEdit();
            else if (ProfileOpen) ProfileOpen = false;
            else if (SwitcherOpen) CloseJump();
            else if (ChannelEditor is { } editor) editor.Cancel.Execute(null);
            else if (ShowSettings) ShowSettings = false;
            else if (SearchOpen) { SearchOpen = false; SearchQuery = ""; }
            else Status = "";
        });
        InitializeKeyboard();
        InitializeMentions();
        PropertyChanged += OnPresentationChanged;
        Channels.CollectionChanged += OnPresentationCollection;
        Messages.CollectionChanged += OnPresentationCollection;
        SeedLayoutFixtures();
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
            SearchQuery = "";
            CloseJump();
        }
        if (args.PropertyName == nameof(SelectedChannel))
        {
            FlushDraft();
            if (IsEditing) { _editingId = null; _editBackup = ""; Changed(nameof(IsEditing)); }
            if (IsReplying) CancelReply();
            RefreshTyping();
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
            CloseMention();
            Changed(nameof(ComposerPlaceholder));
            Changed(nameof(SendTip));
            Changed(nameof(ShowChannelEmpty));
            Changed(nameof(ShowJumpBar));
            Changed(nameof(ChannelHasUnread));
            Changed(nameof(InSelectedVoice));
            if (SearchOpen) FocusSearch?.Invoke();
            else if (!SwitcherOpen && SelectedChannel is { CanChat: true }) FocusComposer?.Invoke();
            if (SelectedChannel is { } opened) NoteVisit(opened.Id);
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
                Changed(nameof(DirectChannels)); Changed(nameof(HasDirectChannels)); Changed(nameof(ShowChannelNav));
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
        var matches = FixtureRows()
            .Concat(Messages)
            .Where(row => string.IsNullOrWhiteSpace(SearchQuery)
                || row.Content.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                || row.Author.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();
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
        foreach (var row in matches)
        {
            if (row.IsUnreadDivider) continue;
            row.Profile = ProfileOf(row.Item.Message.AuthorId, row.Author);
            row.SetMentioned(MentionsMe(row));
        }
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
        Changed(nameof(ChannelForbidden));
        Changed(nameof(CanSend));
        Changed(nameof(ChannelHasUnread));
        Changed(nameof(ShowJumpBar));
    }

    private void InsertUnreadDivider(List<MessageRow> matches)
    {
        if (SearchQuery.Length > 0 || SelectedChannel is not { CanChat: true } channel) return;
        if (!_inbox.TryGetValue(channel.Id, out var inbox) || inbox.LastReadId is not Guid read) return;
        var firstUnread = -1;
        for (var i = 0; i < matches.Count; i++)
            if (!matches[i].IsPending && !matches[i].IsFixture && !matches[i].IsOwn
                && MessageMarkup.IdAfter(matches[i].Id, read))
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
            .Take(45)
            .Select(row =>
            {
                var id = row.Item.Message.AuthorId;
                var user = session?.User(id);
                var name = user?.DisplayName ?? row.Author;
                var username = user?.Username ?? "";
                var isSelf = me is Guid self && self == id;
                var existing = Participants.FirstOrDefault(item => item.Id == id);
                if (existing is null) existing = new MemberProfile(id, name, username, isSelf, isOnline: true);
                else existing.Update(name, username, isSelf, true);
                existing.Playback = _playbacks.TryGetValue(id, out var cached) ? cached.Playback : row.Playback;
                existing.BannerPlayback = _banners.TryGetValue(id, out var banner) ? banner.Playback : existing.BannerPlayback;
                return existing;
            })
            .ToList();
        if (session is not null && me is Guid selfId && next.All(item => item.Id != selfId))
        {
            var self = session.Me;
            var existing = Participants.FirstOrDefault(item => item.Id == selfId)
                ?? new MemberProfile(selfId, self.DisplayName, self.Username, true, isOnline: true);
            existing.Update(self.DisplayName, self.Username, true, true);
            existing.Playback = _playbacks.TryGetValue(selfId, out var selfCached) ? selfCached.Playback : AccountPlayback;
            existing.BannerPlayback = _banners.TryGetValue(selfId, out var selfBanner) ? selfBanner.Playback : existing.BannerPlayback;
            next.Add(existing);
        }
        foreach (var seed in MemberFixtures.Seeds)
        {
            if (next.Any(item => item.Id == seed.Id
                || item.Username.Equals(seed.Username, StringComparison.OrdinalIgnoreCase)
                || item.Name.Equals(seed.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var existing = Participants.FirstOrDefault(item => item.Id == seed.Id)
                ?? new MemberProfile(seed.Id, seed.Name, seed.Username, false, isOnline: seed.Online, isFixture: true);
            existing.Update(seed.Name, seed.Username, false, seed.Online);
            next.Add(existing);
        }
        next = next
            .OrderByDescending(item => item.IsOnline)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(50)
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
        var online = Participants.Where(item => item.IsOnline).ToArray();
        var offline = Participants.Where(item => !item.IsOnline).ToArray();
        var sections = new List<MemberSection>(2);
        if (online.Length > 0) sections.Add(new MemberSection(_text.Get(TextKey.OnlineCount, online.Length), online));
        if (offline.Length > 0) sections.Add(new MemberSection(_text.Get(TextKey.OfflineCount, offline.Length), offline));
        ParticipantSections = sections;
        Changed(nameof(ParticipantCount));
        Changed(nameof(HasParticipants));
        Changed(nameof(ParticipantSections));
    }

    private IEnumerable<MessageRow> FixtureRows() =>
        IsUltraLightParked || SelectedChannel is not { Kind: "text" }
            ? []
            : MessageFixtures.Rows;

    private void SeedLayoutFixtures()
    {
        if (IsUltraLightParked || Channels.Any(item => !item.IsFixture)) return;
        if (Channels.Count == 0)
        {
            var server = Guid.Parse("01900000-0000-7000-8000-000000000100");
            var general = new ChannelItem(Guid.Parse("01900000-0000-7000-8000-000000000101"), server, "日常", "text", null, true);
            var design = new ChannelItem(Guid.Parse("01900000-0000-7000-8000-000000000102"), server, "设计", "text", null, true);
            var voice = new ChannelItem(Guid.Parse("01900000-0000-7000-8000-000000000103"), server, "深夜电台", "voice", "studio", true);
            voice.SyncMembers(
            [
                VoiceRow(MemberFixtures.Seeds[0], muted: true, deafened: false),
                VoiceRow(MemberFixtures.Seeds[1], muted: false, deafened: false),
                VoiceRow(MemberFixtures.Seeds[2], muted: false, deafened: true),
            ]);
            Channels.Add(general);
            Channels.Add(design);
            Channels.Add(voice);
            SelectedChannel = general;
        }
        RefreshMessagePresentation();
        Changed(nameof(ShowChannelNav));
        Changed(nameof(ShowChat));
        Changed(nameof(ShowAuth));
    }

    private static VoiceMemberRow VoiceRow(
        (Guid Id, string Name, string Username, bool Online) seed, bool muted, bool deafened)
    {
        var profile = new MemberProfile(seed.Id, seed.Name, seed.Username, false, isOnline: true, isFixture: true);
        return new VoiceMemberRow(seed.Id, seed.Name, seed.Username, muted, deafened, false, profile);
    }

    public void MentionMember(MemberProfile member)
    {
        var token = member.MentionToken;
        if (string.IsNullOrEmpty(token)) return;
        CloseMention();
        if (Draft.Length > 0 && !char.IsWhiteSpace(Draft[^1]))
            AppendDraft(" ");
        AppendDraft(token + " ");
    }

    private bool MentionsMe(MessageRow row)
    {
        if (row.IsUnreadDivider || row.IsOwn) return false;
        var me = SelectedInstance?.Context.Session?.Me;
        if (me is null) return false;
        return row.Item.Message.Mentions.Contains(me.Id)
            || MessageMarkup.MentionsAccount(row.Content, me.Username,
                row.Item.Message.MentionEveryone, row.Item.Message.MentionHere, me.DisplayName);
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
        _typingTimer?.Stop();
        _typingTimer = null;
        _typing.Clear();
        PropertyChanged -= OnPresentationChanged;
        Channels.CollectionChanged -= OnPresentationCollection;
        Messages.CollectionChanged -= OnPresentationCollection;
        CloseJump();
        _channelDrafts.Clear();
        _inbox.Clear();
    }
}
