using Chat.Core;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.Protocol;

namespace Chat.Core.Messaging;

public sealed class OutboundQueue : IAsyncDisposable
{
    private readonly CacheScope _scope;
    private readonly Guid _accountId;
    private readonly IChatApi _api;
    private readonly IMessageCache _cache;
    private readonly List<OutboundJob> _jobs = [];
    private readonly Dictionary<Guid, SemaphoreSlim> _channels = [];
    private readonly SemaphoreSlim _global = new(MemoryBudget.OutboundInFlight, MemoryBudget.OutboundInFlight);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private int _pumps;

    public OutboundQueue(CacheScope scope, Guid accountId, IChatApi api, IMessageCache cache)
    {
        _scope = scope;
        _accountId = accountId;
        _api = api;
        _cache = cache;
    }

    public event Action<Guid>? Changed;
    public bool CanEnqueue { get { lock (_gate) return _jobs.Count < MemoryBudget.PendingSends; } }

    public IReadOnlyList<TimelineItem> ForChannel(Guid channelId)
    {
        lock (_gate)
            return _jobs.Where(job => job.Row.Message.ChannelId == channelId).Select(job => job.Row).ToArray();
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var records = await _cache.LoadOutboxAsync(_scope, cancellationToken);
        foreach (var record in records)
        {
            if (record.Files.Any(file => file.Path is null))
            {
                await _cache.RemoveOutboxAsync(_scope, record.LocalId, cancellationToken);
                await _cache.DeleteOutboxFilesAsync(_scope, record.LocalId, cancellationToken);
                continue;
            }
            var row = Hydrate(record);
            lock (_gate) _jobs.Add(new OutboundJob(row));
            Changed?.Invoke(record.ChannelId);
            if (row.Status == SendStatus.Sending)
                Pump(row.LocalId);
        }
    }

    public async Task EnqueueAsync(TimelineItem row, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_jobs.Count >= MemoryBudget.PendingSends)
                throw new ClientFault(TextKey.OutboxFull);
            if (_jobs.Exists(job => job.Row.LocalId == row.LocalId))
                return;
            _jobs.Add(new OutboundJob(row));
        }
        try
        {
            await StageAsync(row, cancellationToken);
            await _cache.SaveOutboxAsync(_scope, Snapshot(row), cancellationToken);
        }
        catch
        {
            Remove(row.LocalId, dispose: true);
            throw;
        }
        Changed?.Invoke(row.Message.ChannelId);
        Pump(row.LocalId);
    }

    public Task RetryAsync(Guid localId, CancellationToken cancellationToken)
    {
        OutboundJob? job;
        lock (_gate)
            job = _jobs.Find(item => item.Row.LocalId == localId && item.Row.Status == SendStatus.Failed);
        if (job is null) throw new InvalidOperationException("Nothing to retry.");
        job.Row.Status = SendStatus.Sending;
        Changed?.Invoke(job.Row.Message.ChannelId);
        Pump(localId);
        return Task.CompletedTask;
    }

    public async Task CancelAsync(Guid localId, CancellationToken cancellationToken)
    {
        var job = Remove(localId, dispose: false);
        if (job is null) return;
        job.Work?.Cancel();
        Release(job.Row);
        await _cache.RemoveOutboxAsync(_scope, localId, cancellationToken);
        await _cache.DeleteOutboxFilesAsync(_scope, localId, cancellationToken);
        Changed?.Invoke(job.Row.Message.ChannelId);
    }

    public void Acknowledge(MessageDto message)
    {
        OutboundJob? job;
        lock (_gate)
            job = _jobs.Find(item => Same(item.Row, message));
        if (job is null) return;
        job.Remote = message;
        if (job.Row.Status == SendStatus.Sending && job.Work is not null) return;
        _ = CompleteAsync(job, message);
    }

    public async Task DropChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        OutboundJob[] jobs;
        lock (_gate)
            jobs = _jobs.Where(job => job.Row.Message.ChannelId == channelId).ToArray();
        foreach (var job in jobs)
            await CancelAsync(job.Row.LocalId, cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        OutboundJob[] jobs;
        lock (_gate) jobs = [.. _jobs];
        foreach (var job in jobs)
            await CancelAsync(job.Row.LocalId, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_pumps == 0 && _jobs.TrueForAll(job => job.Row.Status != SendStatus.Sending))
                    return;
            }
            await Task.Delay(15, cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        OutboundJob[] jobs;
        lock (_gate) jobs = [.. _jobs];
        foreach (var job in jobs)
            job.Work?.Cancel();
        var end = DateTime.UtcNow.AddSeconds(2);
        while (Volatile.Read(ref _pumps) > 0 && DateTime.UtcNow < end)
            await Task.Delay(20);
        _lifetime.Dispose();
        _global.Dispose();
        foreach (var channel in _channels.Values)
            channel.Dispose();
    }

    private void Pump(Guid localId)
    {
        _ = Task.Run(() => PushAsync(localId, _lifetime.Token));
    }

    private async Task PushAsync(Guid localId, CancellationToken cancellationToken)
    {
        var job = Find(localId);
        if (job is null) return;
        Interlocked.Increment(ref _pumps);
        var channel = Gate(job.Row.Message.ChannelId);
        var heldChannel = false;
        var heldGlobal = false;
        try
        {
            await channel.WaitAsync(cancellationToken);
            heldChannel = true;
            await _global.WaitAsync(cancellationToken);
            heldGlobal = true;
            job = Find(localId);
            if (job is null || job.Row.Status == SendStatus.Sent) return;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            job.Work = work;
            job.Row.Status = SendStatus.Sending;
            Changed?.Invoke(job.Row.Message.ChannelId);
            if (job.Remote is { } early)
            {
                await CompleteAsync(job, early);
                return;
            }
            while (job.Row.Uploaded.Count < job.Row.Outbox.Count)
            {
                var file = job.Row.Outbox[job.Row.Uploaded.Count];
                if (file.Content.CanSeek) file.Content.Position = 0;
                var dto = await _api.UploadAsync(file, work.Token);
                job.Row.Uploaded.Add(dto.Id);
                await _cache.SaveOutboxAsync(_scope, Snapshot(job.Row), work.Token);
            }
            if (job.Remote is { } uploaded)
            {
                await CompleteAsync(job, uploaded);
                return;
            }
            var sent = await _api.SendMessageAsync(job.Row.Message.ChannelId,
                new(job.Row.Message.Content, job.Row.Message.ReplyTo, [.. job.Row.Uploaded]),
                job.Row.Idempotency, work.Token);
            await CompleteAsync(job, sent);
        }
        catch (OperationCanceledException) when (Find(localId) is null)
        {
        }
        catch
        {
            job = Find(localId);
            if (job is null) return;
            job.Row.Status = SendStatus.Failed;
            foreach (var file in job.Row.Outbox)
                if (file.Content.CanSeek) file.Content.Position = 0;
            try { await _cache.SaveOutboxAsync(_scope, Snapshot(job.Row), CancellationToken.None); }
            catch { /* keep memory row */ }
            Changed?.Invoke(job.Row.Message.ChannelId);
        }
        finally
        {
            if (job is not null) job.Work = null;
            if (heldGlobal) _global.Release();
            if (heldChannel) channel.Release();
            Interlocked.Decrement(ref _pumps);
        }
    }

    private async Task CompleteAsync(OutboundJob job, MessageDto sent)
    {
        await _cache.UpsertAsync(_scope, sent, CancellationToken.None);
        await _cache.RemoveOutboxAsync(_scope, job.Row.LocalId, CancellationToken.None);
        await _cache.DeleteOutboxFilesAsync(_scope, job.Row.LocalId, CancellationToken.None);
        job.Row.Message = sent;
        job.Row.Status = SendStatus.Sent;
        Release(job.Row);
        Remove(job.Row.LocalId, dispose: false);
        Changed?.Invoke(sent.ChannelId);
    }

    private async Task StageAsync(TimelineItem row, CancellationToken cancellationToken)
    {
        if (row.Outbox.Count == 0) return;
        var staged = new List<PickedFile>(row.Outbox.Count);
        var files = new List<OutboxFile>(row.Outbox.Count);
        for (var i = 0; i < row.Outbox.Count; i++)
        {
            var file = row.Outbox[i];
            if (!file.Content.CanSeek)
            {
                staged.Add(file);
                files.Add(new(file.FileName, file.MimeType, file.Size, null));
                continue;
            }
            var path = await _cache.WriteOutboxFileAsync(_scope, row.LocalId, i, file.FileName, file.Content,
                cancellationToken);
            staged.Add(new(file.FileName, file.MimeType, File.OpenRead(path), file.Size));
            files.Add(new(file.FileName, file.MimeType, file.Size, path));
            await file.DisposeAsync();
        }
        row.Outbox.Clear();
        row.Outbox.AddRange(staged);
        row.Files.Clear();
        row.Files.AddRange(files);
    }

    private TimelineItem Hydrate(OutboxRecord record)
    {
        var attachments = record.Files.Select(file => new AttachmentDto(Guid.Empty, file.FileName, file.MimeType,
            file.Size, new("http://127.0.0.1/.pending"), null)).ToArray();
        var row = new TimelineItem
        {
            Message = new(record.LocalId, record.ChannelId, _accountId, "text", record.Content, record.CreatedAt,
                null, record.ReplyTo, [], attachments, [], [], null),
            Status = record.Status == "failed" ? SendStatus.Failed : SendStatus.Sending,
            LocalId = record.LocalId,
            Idempotency = record.Idempotency
        };
        row.Uploaded.AddRange(record.Uploaded);
        foreach (var file in record.Files)
        {
            row.Files.Add(file);
            if (file.Path is { Length: > 0 } path && File.Exists(path))
                row.Outbox.Add(new(file.FileName, file.MimeType, File.OpenRead(path), file.Size));
        }
        return row;
    }

    private static OutboxRecord Snapshot(TimelineItem row) =>
        new(row.LocalId, row.Message.ChannelId, row.Idempotency, row.Message.Content, row.Message.ReplyTo,
            row.Message.CreatedAt, row.Status == SendStatus.Failed ? "failed" : "sending", [.. row.Uploaded],
            [.. row.Files]);

    private static bool Same(TimelineItem row, MessageDto message) =>
        row.Message.Id == message.Id
        || row.Status != SendStatus.Sent
            && row.Message.AuthorId == message.AuthorId
            && row.Message.Content == message.Content
            && string.Join('\0', row.Message.Attachments.Select(item => item.FileName))
                == string.Join('\0', message.Attachments.Select(item => item.FileName));

    private OutboundJob? Find(Guid localId)
    {
        lock (_gate) return _jobs.Find(job => job.Row.LocalId == localId);
    }

    private OutboundJob? Remove(Guid localId, bool dispose)
    {
        lock (_gate)
        {
            var index = _jobs.FindIndex(job => job.Row.LocalId == localId);
            if (index < 0) return null;
            var job = _jobs[index];
            _jobs.RemoveAt(index);
            if (dispose) Release(job.Row);
            return job;
        }
    }

    private SemaphoreSlim Gate(Guid channelId)
    {
        lock (_gate)
        {
            if (_channels.TryGetValue(channelId, out var existing)) return existing;
            var created = new SemaphoreSlim(1, 1);
            _channels[channelId] = created;
            return created;
        }
    }

    private static void Release(TimelineItem row)
    {
        foreach (var file in row.Outbox)
            file.Content.Dispose();
        row.Outbox.Clear();
    }

    private sealed class OutboundJob(TimelineItem row)
    {
        public TimelineItem Row { get; } = row;
        public CancellationTokenSource? Work { get; set; }
        public MessageDto? Remote { get; set; }
    }
}
