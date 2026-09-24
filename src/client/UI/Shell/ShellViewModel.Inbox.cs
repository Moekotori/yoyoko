using Avalonia.Threading;
using Chat.Core.Messaging;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.Protocol;
using Chat.UI.Chat;
using Chat.UI.Components;
using ChannelItem = Chat.UI.Channels.ChannelItem;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private readonly Dictionary<Guid, ChannelInbox> _inbox = [];
    private readonly List<TypingPeer> _typing = [];
    private readonly MessageRow _unreadDivider = MessageRow.UnreadDivider();
    private Guid? _editingId;
    private Guid? _replyTo;
    private string _replyAuthor = "";
    private string _editBackup = "";
    private DateTimeOffset _typedAt;
    private int _newWhileAway;
    private bool _awayFromBottom;
    private DispatcherTimer? _draftTimer;
    private DispatcherTimer? _typingTimer;
    private Guid? _draftFlushChannel;
    public ActionCommand JumpPresent { get; private set; } = null!;
    public ActionCommand CancelComposerEdit { get; private set; } = null!;
    public bool IsEditing => _editingId is not null;
    public bool IsReplying => _replyTo is not null;
    public bool HasComposerBanner => IsEditing || IsReplying;
    public string ComposerBannerText => IsEditing
        ? _text.Get(TextKey.EditingMessage)
        : _text.Get(TextKey.ReplyingTo, _replyAuthor);
    public string ComposerCancelTip => _text.Get(IsEditing ? TextKey.CancelEdit : TextKey.CancelReply);
    public string TypingLabel { get; private set; } = "";
    public bool HasTyping => TypingLabel.Length > 0;
    public bool ChannelHasUnread =>
        SelectedChannel is { CanChat: true } channel
        && _inbox.TryGetValue(channel.Id, out var row)
        && SelectedInstance?.Context.Session is { } session
        && row.HasUnread(session.Me.Id);
    public bool ShowJumpBar => ShowChat && (_awayFromBottom || _newWhileAway > 0 || ChannelHasUnread);
    public string JumpBarLabel => _newWhileAway > 0
        ? _text.Get(TextKey.NewMessages, _newWhileAway)
        : _text.Get(TextKey.JumpToPresent);
    public void OnTimelineScroll(bool nearBottom)
    {
        if (!CanObserveTimeline) return;
        _awayFromBottom = !nearBottom;
        if (nearBottom)
        {
            _newWhileAway = 0;
            if (SelectedChannel is { CanChat: true } channel && LatestVisibleId() is Guid latest)
                _ = MarkReadAsync(channel.Id, latest);
        }
        Changed(nameof(ShowJumpBar));
        Changed(nameof(JumpBarLabel));
    }

    public void JumpToPresent()
    {
        _awayFromBottom = false;
        _newWhileAway = 0;
        ScrollToLatest?.Invoke();
        if (SelectedChannel is { CanChat: true } channel && LatestVisibleId() is Guid latest)
            _ = MarkReadAsync(channel.Id, latest);
        Changed(nameof(ShowJumpBar));
        Changed(nameof(JumpBarLabel));
    }

    public void MarkUnreadFrom(MessageRow row)
    {
        if (row.IsUnreadDivider || row.IsFixture || SelectedChannel is not { CanChat: true } channel) return;
        Guid? previous = null;
        foreach (var item in Messages)
        {
            if (item.Id == row.Id) break;
            if (!item.IsUnreadDivider && !item.IsPending && !item.IsFixture) previous = item.Id;
        }
        _ = SetReadAsync(channel.Id, previous);
        _awayFromBottom = true;
        Changed(nameof(ShowJumpBar));
    }

    public void BeginEdit(MessageRow row)
    {
        if (!row.CanEdit) return;
        if (IsReplying) CancelReply();
        if (!IsEditing) _editBackup = Draft;
        _editingId = row.Id;
        Draft = row.Content;
        NotifyComposer();
        FocusComposer?.Invoke();
    }

    public void BeginReply(MessageRow row)
    {
        if (!row.CanReply) return;
        if (IsEditing) CancelEdit();
        _replyTo = row.Id;
        _replyAuthor = row.Author;
        NotifyComposer();
        FocusComposer?.Invoke();
    }

    public void DeleteMessage(MessageRow row)
    {
        if (!row.CanDelete || _timeline is null) return;
        if (IsEditing && _editingId == row.Id) CancelEdit();
        if (IsReplying && _replyTo == row.Id) CancelReply();
        _ = DeleteMessageAsync(row.Id);
    }

    private async Task DeleteMessageAsync(Guid messageId)
    {
        if (_timeline is null) return;
        try { await _timeline.DeleteAsync(messageId, _lifetime); }
        catch (Exception error) { ReportChatError(error); }
    }

    public void NoteComposerActivity()
    {
        if (IsEditing || ChannelForbidden || SelectedChannel is not { CanChat: true } channel) return;
        if (string.IsNullOrWhiteSpace(Draft)) return;
        if (DateTimeOffset.UtcNow - _typedAt < TimeSpan.FromSeconds(3)) return;
        var session = SelectedInstance?.Context.Session;
        if (session is null) return;
        _typedAt = DateTimeOffset.UtcNow;
        _ = StartTypingAsync(session, channel.Id);
    }

    private async Task StartTypingAsync(InstanceSession session, Guid channelId)
    {
        try { await session.StartTypingAsync(channelId, _lifetime); }
        catch (ChatApiException) { }
        catch (OperationCanceledException) { }
    }

    private void NoteTyping(InstanceSession session, TypingDto typing)
    {
        if (typing.UserId == session.Me.Id) return;
        _typing.RemoveAll(item => item.ChannelId == typing.ChannelId && item.UserId == typing.UserId);
        if (_typing.Count >= 16) _typing.RemoveAt(0);
        _typing.Add(new(typing.ChannelId, typing.UserId, typing.DisplayName, DateTimeOffset.UtcNow.AddSeconds(8)));
        if (!IsUltraLightParked) EnsureTypingTimer();
        RefreshTyping();
    }

    private void ForgetTyping(Guid channelId, Guid userId)
    {
        var removed = _typing.RemoveAll(item => item.ChannelId == channelId && item.UserId == userId);
        if (removed > 0) RefreshTyping();
    }

    private void EnsureTypingTimer()
    {
        if (_typingTimer is null)
        {
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _typingTimer.Tick += (_, _) => RefreshTyping();
        }
        if (!_typingTimer.IsEnabled) _typingTimer.Start();
    }

    private void RefreshTyping()
    {
        var now = DateTimeOffset.UtcNow;
        _typing.RemoveAll(item => item.Until <= now);
        if (_typing.Count == 0) _typingTimer?.Stop();
        var channelId = SelectedChannel?.Id;
        var names = channelId is Guid id
            ? _typing.Where(item => item.ChannelId == id).Select(item => item.Name).Distinct().Take(3).ToArray()
            : [];
        TypingLabel = names.Length switch
        {
            0 => "",
            1 => _text.Get(TextKey.TypingOne, names[0]),
            2 => _text.Get(TextKey.TypingTwo, names[0], names[1]),
            _ => _text.Get(TextKey.TypingMany, names[0], names.Length - 1)
        };
        Changed(nameof(TypingLabel));
        Changed(nameof(HasTyping));
    }

    public void BeginEditLast()
    {
        if (IsEditing || !string.IsNullOrWhiteSpace(Draft) || HasPending) return;
        var last = _timeline?.LastOwnSent();
        if (last is null) return;
        var row = Messages.FirstOrDefault(item => item.Id == last.Message.Id);
        if (row is not null) BeginEdit(row);
    }

    public void CancelEdit()
    {
        if (!IsEditing) return;
        _editingId = null;
        Draft = _editBackup;
        _editBackup = "";
        NotifyComposer();
        FocusComposer?.Invoke();
    }

    public void CancelReply()
    {
        if (!IsReplying) return;
        _replyTo = null;
        _replyAuthor = "";
        NotifyComposer();
    }

    public void CancelComposer()
    {
        if (IsEditing) CancelEdit();
        else CancelReply();
    }

    private void NotifyComposer()
    {
        Changed(nameof(IsEditing));
        Changed(nameof(IsReplying));
        Changed(nameof(HasComposerBanner));
        Changed(nameof(ComposerBannerText));
        Changed(nameof(ComposerCancelTip));
        Changed(nameof(CanSend));
    }

    public void SetChannelNotify(ChannelItem channel, ChannelNotify notify)
    {
        var session = SelectedInstance?.Context.Session;
        if (session is null || !channel.CanChat) return;
        _ = SaveNotifyAsync(session, channel.Id, notify);
    }

    public void MarkChannelRead(ChannelItem channel)
    {
        if (LatestId(channel.Id) is Guid latest) _ = MarkReadAsync(channel.Id, latest);
    }

    public void MarkChannelUnread(ChannelItem channel)
    {
        if (!channel.CanChat) return;
        _ = SetReadAsync(channel.Id, null);
    }

    private async Task ReloadInboxAsync(InstanceSession session)
    {
        var rows = await _cache.LoadInboxAsync(session.Scope, _lifetime);
        if (SelectedInstance?.Context.Session != session) return;
        _inbox.Clear();
        foreach (var row in rows)
        {
            _inbox[row.ChannelId] = row;
            if (string.IsNullOrEmpty(row.Draft)) continue;
            var key = DraftKey(session, row.ChannelId);
            if (key is not null) _channelDrafts[key] = row.Draft;
        }
        ApplyInbox();
        if (SelectedChannel is { } selected
            && _draftKey is string current
            && string.IsNullOrEmpty(Draft)
            && _channelDrafts.TryGetValue(current, out var draft))
            Draft = draft;
    }

    private async Task NoteArrivalAsync(InstanceSession session, MessageDto message)
    {
        var mentioned = message.AuthorId != session.Me.Id
            && (message.Mentions.Contains(session.Me.Id)
                || MessageMarkup.MentionsAccount(message.Content, session.Me.Username,
                    message.MentionEveryone, message.MentionHere, session.Me.DisplayName));
        await _cache.NoteArrivalAsync(session.Scope, message, mentioned, _lifetime);
        if (SelectedInstance?.Context.Session != session) return;
        var current = _inbox.GetValueOrDefault(message.ChannelId);
        _inbox[message.ChannelId] = (current ?? new(message.ChannelId, null, null, null, null, ChannelNotify.All, null)) with
        {
            LastMessageId = message.Id,
            LastAuthorId = message.AuthorId,
            LastMentionId = mentioned ? message.Id : current?.LastMentionId
        };
        ApplyInbox();
    }

    private Task MarkReadAsync(Guid channelId, Guid lastReadId) => SetReadAsync(channelId, lastReadId);

    private async Task SetReadAsync(Guid channelId, Guid? lastReadId)
    {
        var session = SelectedInstance?.Context.Session;
        if (session is null) return;
        if (lastReadId is Guid next
            && _inbox.TryGetValue(channelId, out var existing)
            && existing.LastReadId is Guid read
            && !MessageMarkup.IdAfter(next, read))
        {
            ApplyInbox();
            return;
        }
        await _cache.SaveReadAsync(session.Scope, channelId, lastReadId, _lifetime);
        var current = _inbox.GetValueOrDefault(channelId)
            ?? new ChannelInbox(channelId, null, null, null, null, ChannelNotify.All, null);
        _inbox[channelId] = current with { LastReadId = lastReadId is Guid value && value != Guid.Empty ? value : null };
        ApplyInbox();
        if (SelectedChannel?.Id == channelId) RefreshMessagePresentation();
    }

    private async Task SaveNotifyAsync(InstanceSession session, Guid channelId, ChannelNotify notify)
    {
        await _cache.SaveNotifyAsync(session.Scope, channelId, notify, _lifetime);
        if (SelectedInstance?.Context.Session != session) return;
        var current = _inbox.GetValueOrDefault(channelId)
            ?? new ChannelInbox(channelId, null, null, null, null, notify, null);
        _inbox[channelId] = current with { Notify = notify };
        ApplyInbox();
    }

    private void QueueDraftSave()
    {
        if (_draftKey is null || SelectedChannel is not { CanChat: true } channel) return;
        _draftFlushChannel = channel.Id;
        _draftTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _draftTimer.Tick -= OnDraftTick;
        _draftTimer.Tick += OnDraftTick;
        _draftTimer.Stop();
        _draftTimer.Start();
    }

    private void OnDraftTick(object? sender, EventArgs args)
    {
        _draftTimer?.Stop();
        FlushDraft();
    }

    private void FlushDraft()
    {
        var session = SelectedInstance?.Context.Session;
        var channelId = _draftFlushChannel;
        if (session is null || channelId is not Guid id) return;
        var key = DraftKey(session, id);
        var draft = key is not null && _channelDrafts.TryGetValue(key, out var stored) ? stored : "";
        _ = _cache.SaveDraftAsync(session.Scope, id, string.IsNullOrEmpty(draft) ? null : draft, _lifetime);
    }

    private void ApplyInbox()
    {
        var self = SelectedInstance?.Context.Session?.Me.Id ?? Guid.Empty;
        foreach (var channel in Channels)
        {
            if (_inbox.TryGetValue(channel.Id, out var row)) channel.ApplyInbox(row, self);
            else channel.ClearInbox();
        }
        if (SelectedInstance is { } instance)
        {
            instance.HasMention = Channels.Any(channel => channel.HasMention);
            instance.HasUnread = !instance.HasMention && Channels.Any(channel => channel.HasUnread);
        }
        Changed(nameof(ChannelHasUnread));
        Changed(nameof(ShowJumpBar));
        Changed(nameof(JumpBarLabel));
    }

    private Guid? LatestVisibleId()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
            if (!Messages[i].IsPending) return Messages[i].Id;
        return null;
    }

    private Guid? LatestId(Guid channelId) =>
        _inbox.TryGetValue(channelId, out var row) ? row.LastMessageId : LatestVisibleId();

    private void NoteVisit(Guid channelId)
    {
        var session = SelectedInstance?.Context.Session;
        if (session is null) return;
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var current = _inbox.GetValueOrDefault(channelId)
            ?? new ChannelInbox(channelId, null, null, null, null, ChannelNotify.All, null);
        _inbox[channelId] = current with { VisitedAt = at };
        _ = _cache.SaveVisitAsync(session.Scope, channelId, at, _lifetime);
    }

    public MemberProfile ProfileOf(Guid id, string? name = null)
    {
        var existing = Participants.FirstOrDefault(item => item.Id == id);
        if (existing is not null) return existing;
        var session = SelectedInstance?.Context.Session;
        var user = session?.User(id);
        var profile = new MemberProfile(id, user?.DisplayName ?? name ?? id.ToString("N")[..8], user?.Username ?? "",
            session?.Me.Id == id);
        if (_playbacks.TryGetValue(id, out var cached)) profile.Playback = cached.Playback;
        if (_banners.TryGetValue(id, out var banner)) profile.BannerPlayback = banner.Playback;
        return profile;
    }

    private string ReplyPreview(MessageDto message, InstanceSession? session)
    {
        if (message.ReplyTo is not Guid id) return "";
        var parent = _timeline?.Find(id)?.Message;
        if (parent is null) return _text.Get(TextKey.ReplyMissing);
        var author = session?.AuthorName(parent.AuthorId) ?? parent.AuthorId.ToString("N")[..8];
        var snippet = (parent.Content ?? "").Replace('\n', ' ').Trim();
        if (snippet.Length == 0 && parent.Attachments.Length > 0)
            snippet = parent.Attachments[0].FileName;
        if (snippet.Length == 0) return _text.Get(TextKey.ReplyMissing);
        if (snippet.Length > 72) snippet = snippet[..72] + "…";
        return _text.Get(TextKey.ReplyQuote, author, snippet);
    }

    private static string? DraftKey(InstanceSession session, Guid channelId) =>
        $"{session.Descriptor.Id.Value}:{session.Account.Key.Id}:{channelId}";

    private readonly record struct TypingPeer(Guid ChannelId, Guid UserId, string Name, DateTimeOffset Until);
}
