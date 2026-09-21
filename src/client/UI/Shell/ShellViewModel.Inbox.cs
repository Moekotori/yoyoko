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
    private readonly MessageRow _unreadDivider = MessageRow.UnreadDivider();
    private Guid? _editingId;
    private string _editBackup = "";
    private int _newWhileAway;
    private bool _awayFromBottom;
    private DispatcherTimer? _draftTimer;
    private Guid? _draftFlushChannel;
    public ActionCommand JumpPresent { get; private set; } = null!;
    public ActionCommand CancelComposerEdit { get; private set; } = null!;
    public bool IsEditing => _editingId is not null;
    public bool ChannelHasUnread =>
        SelectedChannel is { Kind: "text" } channel
        && _inbox.TryGetValue(channel.Id, out var row)
        && SelectedInstance?.Context.Session is { } session
        && row.HasUnread(session.Me.Id);
    public bool ShowJumpBar => ShowChat && (_awayFromBottom || _newWhileAway > 0 || ChannelHasUnread);
    public string JumpBarLabel => _newWhileAway > 0
        ? _text.Get(TextKey.NewMessages, _newWhileAway)
        : _text.Get(TextKey.JumpToPresent);
    public string ComposerEditHint => _text.Get(TextKey.EditingMessage);

    public void OnTimelineScroll(bool nearBottom)
    {
        _awayFromBottom = !nearBottom;
        if (nearBottom)
        {
            _newWhileAway = 0;
            if (SelectedChannel is { Kind: "text" } channel && LatestVisibleId() is Guid latest)
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
        if (SelectedChannel is { Kind: "text" } channel && LatestVisibleId() is Guid latest)
            _ = MarkReadAsync(channel.Id, latest);
        Changed(nameof(ShowJumpBar));
        Changed(nameof(JumpBarLabel));
    }

    public void MarkUnreadFrom(MessageRow row)
    {
        if (row.IsUnreadDivider || SelectedChannel is not { Kind: "text" } channel) return;
        Guid? previous = null;
        foreach (var item in Messages)
        {
            if (item.Id == row.Id) break;
            if (!item.IsUnreadDivider && !item.IsPending) previous = item.Id;
        }
        _ = SetReadAsync(channel.Id, previous);
        _awayFromBottom = true;
        Changed(nameof(ShowJumpBar));
    }

    public void BeginEdit(MessageRow row)
    {
        if (!row.CanEdit) return;
        if (!IsEditing) _editBackup = Draft;
        _editingId = row.Id;
        Draft = row.Content;
        Changed(nameof(IsEditing));
        Changed(nameof(CanSend));
        FocusComposer?.Invoke();
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
        Changed(nameof(IsEditing));
        Changed(nameof(CanSend));
        FocusComposer?.Invoke();
    }

    public void SetChannelNotify(ChannelItem channel, ChannelNotify notify)
    {
        var session = SelectedInstance?.Context.Session;
        if (session is null || channel.Kind != "text") return;
        _ = SaveNotifyAsync(session, channel.Id, notify);
    }

    public void MarkChannelRead(ChannelItem channel)
    {
        if (LatestId(channel.Id) is Guid latest) _ = MarkReadAsync(channel.Id, latest);
    }

    public void MarkChannelUnread(ChannelItem channel)
    {
        if (channel.Kind != "text") return;
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
                || MessageMarkup.MentionsUser(message.Content, session.Me.Username));
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
        if (_draftKey is null || SelectedChannel is not { Kind: "text" } channel) return;
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

    private static string? DraftKey(InstanceSession session, Guid channelId) =>
        $"{session.Descriptor.Id.Value}:{session.Account.Key.Id}:{channelId}";
}
