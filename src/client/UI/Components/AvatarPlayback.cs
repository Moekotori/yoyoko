using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Chat.UI.Components;

public sealed class AvatarPlayback : IDisposable, INotifyPropertyChanged
{
    private const int MaxFrames = 48;
    private readonly Bitmap[] _frames;
    private readonly int[] _delays;
    private DispatcherTimer? _timer;
    private int _index;
    private int _watchers;
    private bool _windowPaused;
    private bool _disposed;
    public event PropertyChangedEventHandler? PropertyChanged;
    private AvatarPlayback(Bitmap[] frames, int[] delays)
    {
        _frames = frames;
        _delays = delays;
        Current = frames[0];
        IsAnimated = frames.Length > 1;
    }
    public Bitmap Current { get; private set; }
    public bool IsAnimated { get; }
    public long DecodedBytes => _frames.Sum(frame => (long)frame.PixelSize.Width * frame.PixelSize.Height * 4);

    public static AvatarPlayback Decode(byte[] bytes, int maxEdge, bool allowAnimation = true, int maxFrames = MaxFrames)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Invalid avatar.");
        if (allowAnimation && codec.FrameCount > 1)
        {
            try { return DecodeMotion(codec, maxEdge, Math.Clamp(maxFrames, 1, MaxFrames)); }
            catch { /* A failed animation falls back to a bounded first frame. */ }
        }
        using var input = new MemoryStream(bytes, writable: false);
        return new([codec.Info.Width >= codec.Info.Height
            ? Bitmap.DecodeToWidth(input, Math.Min(maxEdge, codec.Info.Width))
            : Bitmap.DecodeToHeight(input, Math.Min(maxEdge, codec.Info.Height))], [0]);
    }

    public void AddWatcher()
    {
        _watchers++;
        SyncTimer();
    }

    public void RemoveWatcher()
    {
        if (_watchers > 0) _watchers--;
        SyncTimer();
    }

    public void SetWindowPaused(bool paused)
    {
        _windowPaused = paused;
        SyncTimer();
    }

    private static AvatarPlayback DecodeMotion(SKCodec codec, int maxEdge, int maxFrames)
    {
        var info = codec.Info;
        var scale = Math.Min(1f, maxEdge / (float)Math.Max(info.Width, info.Height));
        var width = Math.Max(1, (int)(info.Width * scale));
        var height = Math.Max(1, (int)(info.Height * scale));
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var count = Math.Min(codec.FrameCount, maxFrames);
        var frames = new Bitmap[count];
        var delays = new int[count];
        var frameInfo = codec.FrameInfo;
        using var sk = new SKBitmap(imageInfo);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var result = codec.GetPixels(imageInfo, sk.GetPixels(), new SKCodecOptions(i));
                if (result != SKCodecResult.Success) throw new InvalidDataException("Invalid avatar frame.");
                frames[i] = ToAvalonia(sk);
                var delay = frameInfo is { Length: > 0 } ? frameInfo[Math.Min(i, frameInfo.Length - 1)].Duration : 100;
                delays[i] = delay <= 0 ? 100 : Math.Clamp(delay, 20, 4_000);
            }
            return new(frames, delays);
        }
        catch
        {
            foreach (var frame in frames) frame?.Dispose();
            throw;
        }
    }

    private static Bitmap ToAvalonia(SKBitmap source)
    {
        var bitmap = new WriteableBitmap(new PixelSize(source.Width, source.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bitmap.Lock();
        var length = source.RowBytes * source.Height;
        var pixels = new byte[length];
        Marshal.Copy(source.GetPixels(), pixels, 0, length);
        if (fb.RowBytes == source.RowBytes)
            Marshal.Copy(pixels, 0, fb.Address, length);
        else
        {
            var stride = source.Width * 4;
            for (var y = 0; y < source.Height; y++)
                Marshal.Copy(pixels, y * source.RowBytes, IntPtr.Add(fb.Address, y * fb.RowBytes), stride);
        }
        return bitmap;
    }

    private void SyncTimer()
    {
        if (_disposed || !IsAnimated || _watchers == 0 || _windowPaused)
        {
            _timer?.Stop();
            return;
        }
        _timer ??= new DispatcherTimer { Tag = this };
        _timer.Tick -= Tick;
        _timer.Tick += Tick;
        _timer.Interval = TimeSpan.FromMilliseconds(_delays[_index]);
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_disposed || _frames.Length == 0) return;
        _index = (_index + 1) % _frames.Length;
        Current = _frames[_index];
        PropertyChanged?.Invoke(this, new(nameof(Current)));
        if (_timer is not null) _timer.Interval = TimeSpan.FromMilliseconds(_delays[_index]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Stop();
        _timer = null;
        foreach (var frame in _frames) frame.Dispose();
    }
}
