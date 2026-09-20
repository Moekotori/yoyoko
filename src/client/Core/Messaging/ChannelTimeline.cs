using Chat.Core.Sessions;
using Chat.Protocol;

namespace Chat.Core.Messaging;

public enum SendStatus { Sent, Sending, Failed }

public sealed class TimelineItem
{
    public required MessageDto Message { get; set; }
    public SendStatus Status { get; set; }
    public Guid LocalId { get; set; }
}

public sealed class ChannelTimeline(CacheScope scope, Guid channelId, Guid accountId, IChatApi api, IMessageCache cache)
{
    private readonly List<TimelineItem> _items = [];
    public IReadOnlyList<TimelineItem> Items => _items;
    public bool HasOlder { get; private set; }
    public event Action? Changed;

    public async Task LoadLatestAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.ReadPageAsync(scope, channelId, null, MemoryBudget.PageSize, cancellationToken);
        Replace(cached.Items.Reverse());
        HasOlder = cached.Before is not null;
        Changed?.Invoke();
        try
        {
            var remote = await api.ListMessagesAsync(channelId, null, MemoryBudget.PageSize, cancellationToken);
            foreach (var item in remote.Items)
                await cache.UpsertAsync(scope, item, cancellationToken);
            var fresh = await cache.ReadPageAsync(scope, channelId, null, MemoryBudget.PageSize, cancellationToken);
            Replace(fresh.Items.Reverse());
            HasOlder = fresh.Before is not null;
            Changed?.Invoke();
        }
        catch { /* cache already shown */ }
    }

    public async Task LoadOlderAsync(CancellationToken cancellationToken)
    {
        if (_items.Count == 0 || !HasOlder) return;
        var oldest = _items[0].Message.Id;
        var page = await cache.ReadPageAsync(scope, channelId, oldest, MemoryBudget.PageSize, cancellationToken);
        if (page.Items.Count == 0)
        {
            try
            {
                var remote = await api.ListMessagesAsync(channelId, oldest, MemoryBudget.PageSize, cancellationToken);
                page = new MessagePage(remote.Items, remote.Before);
            }
            catch { return; }
            foreach (var item in page.Items) await cache.UpsertAsync(scope, item, cancellationToken);
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

    public async Task SendAsync(string? content, IReadOnlyList<PickedFile> files, CancellationToken cancellationToken)
    {
        var localId = Guid.CreateVersion7();
        var localFiles = files.Select(file => new AttachmentDto(Guid.Empty, file.FileName, file.MimeType, file.Size,
            new("http://127.0.0.1/.pending"), null)).ToArray();
        var pending = new MessageDto(localId, channelId, accountId, "text", content, DateTimeOffset.UtcNow, null, null,
            [], localFiles, [], [], null);
        var row = new TimelineItem { Message = pending, Status = SendStatus.Sending, LocalId = localId };
        _items.Add(row);
        Trim();
        Changed?.Invoke();
        try
        {
            var uploaded = new List<Guid>();
            foreach (var file in files)
            {
                var dto = await api.UploadAsync(file, cancellationToken);
                uploaded.Add(dto.Id);
            }
            var sent = await api.SendMessageAsync(channelId, new(content, null, [.. uploaded]), localId.ToString("N")[..16], cancellationToken);
            await cache.UpsertAsync(scope, sent, cancellationToken);
            row.Message = sent;
            row.Status = SendStatus.Sent;
            Changed?.Invoke();
        }
        catch
        {
            row.Status = SendStatus.Failed;
            Changed?.Invoke();
            throw;
        }
    }

    public void ApplyRemote(MessageDto message)
    {
        if (message.ChannelId != channelId) return;
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

    private void Replace(IEnumerable<MessageDto> messages)
    {
        var pending = _items.Where(item => item.Status != SendStatus.Sent).ToList();
        _items.Clear();
        foreach (var message in messages)
            _items.Add(new() { Message = message, Status = SendStatus.Sent, LocalId = message.Id });
        foreach (var item in pending)
            if (!_items.Exists(existing => existing.Message.Id == item.Message.Id))
                _items.Add(item);
        Trim();
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
