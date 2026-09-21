using Avalonia.Media.Imaging;
using Chat.Core.Messaging;
using SkiaSharp;

namespace Chat.UI.Chat;

// UI-thread owned. Rows borrow images; this scope owns and disposes every bitmap.
public sealed class AttachmentPreviews(CancellationToken lifetime) : IDisposable
{
    private sealed class Entry(Uri url)
    {
        public Uri Url { get; } = url;
        public List<AttachmentRow> Rows { get; } = [];
        public Bitmap? Image { get; set; }
        public bool Attempted { get; set; }
    }

    private readonly Dictionary<Uri, Entry> _entries = [];
    private CancellationTokenSource _scope = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    private Func<Uri, int, CancellationToken, Task<byte[]?>>? _download;
    private int _workers;
    private bool _disposed;
    private long _budget = MemoryBudget.ThumbnailBytes;
    private int _concurrency = 4;
    public long RetainedBytes { get; private set; }

    public void Update(IEnumerable<AttachmentRow> rows,
        Func<Uri, int, CancellationToken, Task<byte[]?>> download)
    {
        if (_disposed || lifetime.IsCancellationRequested) return;
        _download = download;
        var current = rows.Where(row => row.PreviewUrl is not null)
            .GroupBy(row => row.PreviewUrl!).ToDictionary(group => group.Key, group => group.ToList());
        foreach (var entry in _entries.Values.ToArray())
        {
            current.TryGetValue(entry.Url, out var consumers);
            foreach (var row in entry.Rows)
                if (consumers is null || !consumers.Contains(row)) row.Preview = null;
            entry.Rows.Clear();
            if (consumers is not null) continue;
            Release(entry);
            _entries.Remove(entry.Url);
        }
        foreach (var (url, consumers) in current)
        {
            if (!_entries.TryGetValue(url, out var entry))
                _entries.Add(url, entry = new(url));
            entry.Rows.AddRange(consumers);
            foreach (var row in consumers)
                if (!ReferenceEquals(row.Preview, entry.Image)) row.Preview = entry.Image;
        }
        StartWorkers();
    }

    public void SetBudget(long bytes, int concurrency)
    {
        if (_disposed) return;
        _budget = Math.Clamp(bytes, 0, MemoryBudget.ThumbnailBytes);
        _concurrency = Math.Clamp(concurrency, 0, 4);
        if (_budget == 0 || _concurrency == 0) { Clear(); return; }
        foreach (var entry in _entries.Values.Reverse())
        {
            if (RetainedBytes > _budget) Release(entry, keepRows: true);
            if (entry.Image is null) entry.Attempted = false;
        }
        StartWorkers();
    }

    private void StartWorkers()
    {
        // Fixed workers scan the latest bounded timeline; no task per row or refresh.
        while (!_disposed && !lifetime.IsCancellationRequested && _budget > 0
            && _budget - RetainedBytes >= 128 * 128 * 4
            && _workers < _concurrency && _entries.Values.Any(entry => !entry.Attempted))
        {
            _workers++;
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_disposed && !lifetime.IsCancellationRequested && _workers <= _concurrency && _budget > 0)
            {
                if (_budget - RetainedBytes < 128 * 128 * 4) return;
                var entry = _entries.Values.FirstOrDefault(item => !item.Attempted);
                if (entry is null) return;
                entry.Attempted = true;
                var token = _scope.Token;
                try
                {
                    var bytes = await _download!(entry.Url, 512 * 1024, token);
                    if (bytes is null || token.IsCancellationRequested || !IsCurrent(entry)) continue;
                    var bitmap = Decode(bytes);
                    var size = Size(bitmap);
                    if (size > _budget - RetainedBytes)
                    {
                        bitmap.Dispose();
                        continue; // The downloadable attachment chip remains available.
                    }
                    entry.Image = bitmap;
                    RetainedBytes += size;
                    foreach (var row in entry.Rows) row.Preview = bitmap;
                }
                catch { /* Cancellation or invalid image: retain the attachment chip. */ }
            }
        }
        finally { _workers--; }
    }

    private bool IsCurrent(Entry entry) => !_disposed
        && _entries.TryGetValue(entry.Url, out var current) && ReferenceEquals(current, entry);

    private static Bitmap Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Invalid thumbnail.");
        using var stream = new MemoryStream(bytes, writable: false);
        // Bound both axes: a narrow portrait must not decode to an unbounded height.
        return codec.Info.Width >= codec.Info.Height
            ? Bitmap.DecodeToWidth(stream, Math.Min(128, codec.Info.Width))
            : Bitmap.DecodeToHeight(stream, Math.Min(128, codec.Info.Height));
    }

    private static long Size(Bitmap bitmap) => (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;

    private void Release(Entry entry, bool keepRows = false)
    {
        foreach (var row in entry.Rows) row.Preview = null;
        if (!keepRows) entry.Rows.Clear();
        if (entry.Image is not { } image) return;
        RetainedBytes -= Size(image);
        entry.Image = null;
        image.Dispose();
    }

    public void Clear()
    {
        if (_disposed) return;
        foreach (var entry in _entries.Values) Release(entry);
        _entries.Clear();
        _download = null;
        // Cancel only after removing pending work: cancellation continuations may run inline.
        var previous = _scope;
        _scope = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
        _scope.Dispose();
    }
}
