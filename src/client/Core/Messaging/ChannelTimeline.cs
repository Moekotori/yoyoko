using Chat.Core.Sessions;
using Chat.Protocol;

namespace Chat.Core.Messaging;

public enum SendStatus { Sent, Sending, Failed }

public sealed class TimelineItem
{
    public required MessageDto Message { get; set; }
    public SendStatus Status { get; set; }
    public Guid LocalId { get; set; }
    public string Idempotency { get; set; } = "";
    public List<PickedFile> Outbox { get; } = [];
    public List<Guid> Uploaded { get; } = [];
    public List<OutboxFile> Files { get; } = [];
}

public sealed class ChannelTimeline
{
    private readonly CacheScope _scope;
    private readonly Guid _channelId;
    private readonly Guid _accountId;
    private readonly IChatApi _api;
    private readonly IMessageCache _cache;
    private readonly OutboundQueue? _outbound;
    private readonly List<TimelineItem> _items = [];

    public ChannelTimeline(CacheScope scope, Guid channelId, Guid accountId, IChatApi api, IMessageCache cache,
        OutboundQueue? outbound = null)
    {
        _scope = scope;
        _channelId = channelId;
        _accountId = accountId;
        _api = api;
        _cache = cache;
        _outbound = outbound;
        if (_outbound is not null)
            _outbound.Changed += OnOutbound;
    }

    public IReadOnlyList<TimelineItem> Items => _items;
    public bool HasOlder { get; private set; }
    public bool AccessDenied { get; private set; }
    public bool CanQueue => _outbound?.CanEnqueue ?? true;
    public event Action? Changed;

    public async Task LoadLatestAsync(CancellationToken cancellationToken)
    {
        AccessDenied = false;
        var cached = await _cache.ReadPageAsync(_scope, _channelId, null, MemoryBudget.PageSize, cancellationToken);
        Replace(cached.Items.Reverse());
        HasOlder = cached.Before is not null;
        Changed?.Invoke();
        try
        {
            var remote = await _api.ListMessagesAsync(_channelId, null, MemoryBudget.PageSize, cancellationToken);
            foreach (var item in remote.Items)
                await _cache.UpsertAsync(_scope, item, cancellationToken);
            var fresh = await _cache.ReadPageAsync(_scope, _channelId, null, MemoryBudget.PageSize, cancellationToken);
            Replace(fresh.Items.Reverse());
            HasOlder = fresh.Before is not null;
            Changed?.Invoke();
        }
        catch (ChatApiException exception) when (exception.Code is "forbidden" or "not_found")
        {
            await DenyAsync(cancellationToken);
        }
        catch { /* cache already shown */ }
    }

    public async Task LoadOlderAsync(CancellationToken cancellationToken)
    {
        if (_items.Count == 0 || !HasOlder) return;
        var oldest = _items[0].Message.Id;
        var page = await _cache.ReadPageAsync(_scope, _channelId, oldest, MemoryBudget.PageSize, cancellationToken);
        if (page.Items.Count == 0)
        {
            try
            {
                var remote = await _api.ListMessagesAsync(_channelId, oldest, MemoryBudget.PageSize, cancellationToken);
                page = new MessagePage(remote.Items, remote.Before);
            }
            catch (ChatApiException exception) when (exception.Code is "forbidden" or "not_found")
            {
                await DenyAsync(cancellationToken);
                return;
            }
            catch { return; }
            foreach (var item in page.Items) await _cache.UpsertAsync(_scope, item, cancellationToken);
        }
        HasOlder = page.Before is not null;
        foreach (var item in page.Items.Reverse())
        {
            if (_items.Exists(existing => existing.Message.Id == item.Id)) continue;
            _items.Insert(0, new() { Message = item, Status = SendStatus.Sent, LocalId = item.Id });
        }
        Trim();
        Changed?.Invoke();
    }

    public async Task SendAsync(string? content, IReadOnlyList<PickedFile> files, Guid? replyTo, CancellationToken cancellationToken)
    {
        var localId = Guid.CreateVersion7();
        var localFiles = files.Select(file => new AttachmentDto(Guid.Empty, file.FileName, file.MimeType, file.Size,
            new("http://127.0.0.1/.pending"), null)).ToArray();
        var pending = new MessageDto(localId, _channelId, _accountId, "text", content, DateTimeOffset.UtcNow, null, replyTo,
            [], localFiles, [], [], null);
        var row = new TimelineItem
        {
            Message = pending,
            Status = SendStatus.Sending,
            LocalId = localId,
            Idempotency = localId.ToString("N")[..16]
        };
        row.Outbox.AddRange(files);
        _items.Add(row);
        Trim();
        Changed?.Invoke();
        try
        {
            if (_outbound is not null)
                await _outbound.EnqueueAsync(row, cancellationToken);
            else
                await PushAsync(row, cancellationToken);
        }
        catch
        {
            if (row.Status != SendStatus.Failed)
            {
                _items.Remove(row);
                Changed?.Invoke();
            }
            throw;
        }
    }

    public Task RetryAsync(Guid localId, CancellationToken cancellationToken)
    {
        if (_outbound is not null)
            return _outbound.RetryAsync(localId, cancellationToken);
        var row = _items.Find(item => item.LocalId == localId && item.Status == SendStatus.Failed)
            ?? throw new InvalidOperationException("Nothing to retry.");
        return PushAsync(row, cancellationToken);
    }

    public Task CancelAsync(Guid localId, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(item => item.LocalId == localId && item.Status != SendStatus.Sent);
        if (index >= 0)
        {
            if (_outbound is null)
                Release(_items[index]);
            _items.RemoveAt(index);
            Changed?.Invoke();
        }
        return _outbound?.CancelAsync(localId, cancellationToken) ?? Task.CompletedTask;
    }

    public void DropFailed(Guid localId)
    {
        _ = CancelAsync(localId, CancellationToken.None);
    }

    public void ReleaseUnsent()
    {
        Detach();
        if (_outbound is not null) return;
        foreach (var item in _items)
            if (item.Status != SendStatus.Sent)
                Release(item);
    }

    public void Detach()
    {
        if (_outbound is not null)
            _outbound.Changed -= OnOutbound;
    }

    private async Task PushAsync(TimelineItem row, CancellationToken cancellationToken)
    {
        if (row.Status != SendStatus.Sending)
        {
            row.Status = SendStatus.Sending;
            Changed?.Invoke();
        }
        try
        {
            while (row.Uploaded.Count < row.Outbox.Count)
            {
                var file = row.Outbox[row.Uploaded.Count];
                if (file.Content.CanSeek) file.Content.Position = 0;
                var dto = await _api.UploadAsync(file, cancellationToken);
                row.Uploaded.Add(dto.Id);
            }
            var sent = await _api.SendMessageAsync(_channelId, new(row.Message.Content, row.Message.ReplyTo, [.. row.Uploaded]),
                row.Idempotency, cancellationToken);
            await _cache.UpsertAsync(_scope, sent, cancellationToken);
            row.Message = sent;
            row.Status = SendStatus.Sent;
            Release(row);
            Changed?.Invoke();
        }
        catch
        {
            row.Status = SendStatus.Failed;
            foreach (var file in row.Outbox)
                if (file.Content.CanSeek) file.Content.Position = 0;
            Changed?.Invoke();
            throw;
        }
    }

    private static void Release(TimelineItem row)
    {
        foreach (var file in row.Outbox)
            file.Content.Dispose();
        row.Outbox.Clear();
    }

    public async Task EditAsync(Guid messageId, string content, CancellationToken cancellationToken)
    {
        var row = _items.Find(item => item.Message.Id == messageId && item.Status == SendStatus.Sent)
            ?? throw new InvalidOperationException("Nothing to edit.");
        if (row.Message.AuthorId != _accountId) throw new InvalidOperationException("Cannot edit that message.");
        var previous = row.Message;
        row.Message = previous with { Content = content, EditedAt = DateTimeOffset.UtcNow };
        Changed?.Invoke();
        try
        {
            var edited = await _api.EditMessageAsync(_channelId, messageId, new(content), cancellationToken);
            await _cache.UpsertAsync(_scope, edited, cancellationToken);
            row.Message = edited;
            row.Status = SendStatus.Sent;
            Changed?.Invoke();
        }
        catch
        {
            row.Message = previous;
            Changed?.Invoke();
            throw;
        }
    }

    public async Task DeleteAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(item => item.Message.Id == messageId && item.Status == SendStatus.Sent);
        if (index < 0) throw new InvalidOperationException("Nothing to delete.");
        var row = _items[index];
        _items.RemoveAt(index);
        Changed?.Invoke();
        try
        {
            await _api.DeleteMessageAsync(_channelId, messageId, cancellationToken);
            await _cache.RemoveMessageAsync(_scope, _channelId, messageId, cancellationToken);
        }
        catch
        {
            Restore(row);
            Changed?.Invoke();
            throw;
        }
    }

    public void RemoveRemote(Guid messageId)
    {
        var index = _items.FindIndex(item => item.Message.Id == messageId);
        if (index < 0) return;
        Release(_items[index]);
        _items.RemoveAt(index);
        Changed?.Invoke();
    }

    public TimelineItem? Find(Guid id) => _items.Find(item => item.Message.Id == id);

    public TimelineItem? LastOwnSent()
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].Status == SendStatus.Sent && _items[i].Message.AuthorId == _accountId
                && _items[i].Message.Kind == "text")
                return _items[i];
        return null;
    }

    public void ApplyRemote(MessageDto message)
    {
        if (message.ChannelId != _channelId) return;
        _outbound?.Acknowledge(message);
        var existing = _items.Find(item =>
            item.Message.Id == message.Id
            || item.Status != SendStatus.Sent
                && item.Message.AuthorId == message.AuthorId
                && item.Message.Content == message.Content
                && Names(item.Message) == Names(message));
        if (existing is not null)
        {
            existing.Message = message;
            existing.Status = SendStatus.Sent;
        }
        else
        {
            _items.Add(new() { Message = message, Status = SendStatus.Sent, LocalId = message.Id });
            Trim();
        }
        Changed?.Invoke();
    }

    private async Task DenyAsync(CancellationToken cancellationToken)
    {
        AccessDenied = true;
        if (_outbound is not null)
            await _outbound.DropChannelAsync(_channelId, cancellationToken);
        else
            foreach (var item in _items)
                if (item.Status != SendStatus.Sent)
                    Release(item);
        _items.Clear();
        HasOlder = false;
        await _cache.ClearChannelAsync(_scope, _channelId, cancellationToken);
        Changed?.Invoke();
    }

    private void Restore(TimelineItem row)
    {
        var index = _items.FindIndex(item => MessageIdOrder(item.Message.Id, row.Message.Id) > 0);
        if (index < 0) _items.Add(row);
        else _items.Insert(index, row);
    }

    private static int MessageIdOrder(Guid left, Guid right) => left.CompareTo(right);

    private void Replace(IEnumerable<MessageDto> messages)
    {
        var pending = _items.Where(item => item.Status != SendStatus.Sent).ToList();
        _items.Clear();
        foreach (var message in messages)
            _items.Add(new() { Message = message, Status = SendStatus.Sent, LocalId = message.Id });
        foreach (var item in pending)
            if (!_items.Exists(existing => existing.Message.Id == item.Message.Id || existing.LocalId == item.LocalId))
                _items.Add(item);
        MergeOutbound();
        Trim();
    }

    private void OnOutbound(Guid channelId)
    {
        if (channelId != _channelId) return;
        MergeOutbound();
        Changed?.Invoke();
    }

    private void MergeOutbound()
    {
        if (_outbound is null) return;
        foreach (var item in _outbound.ForChannel(_channelId))
            if (!_items.Exists(existing => existing.LocalId == item.LocalId || existing.Message.Id == item.Message.Id))
                _items.Add(item);
    }

    private static string Names(MessageDto message) =>
        string.Join('\0', message.Attachments.Select(item => item.FileName));

    private void Trim()
    {
        if (_items.Count <= MemoryBudget.TimelineMessages) return;
        _items.RemoveRange(0, _items.Count - MemoryBudget.TimelineMessages);
        HasOlder = true;
    }
}
