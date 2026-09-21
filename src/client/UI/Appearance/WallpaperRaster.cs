using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace Chat.UI.Appearance;

internal static class WallpaperRaster
{
    public static Bitmap Decode(string path, PixelSize size, int blur)
    {
        size = WallpaperBudget.Clamp(size, false);
        using var bitmap = Render(path, size, blur, 0);
        return WallpaperFrames.FromSkia(bitmap);
    }

    public static WallpaperMotion? TryMotion(string path, PixelSize size, int blur, Action<WriteableBitmap> present)
    {
        var stream = File.OpenRead(path);
        SKCodec? codec = null;
        try
        {
            codec = SKCodec.Create(stream);
            if (codec is null || codec.FrameCount <= 1)
            {
                codec?.Dispose();
                stream.Dispose();
                return null;
            }
            return new WallpaperMotion(stream, codec, size, blur, present);
        }
        catch
        {
            codec?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    internal static SKBitmap Render(string path, PixelSize size, int blur, int frame)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidOperationException("decode");
        return Render(codec, size, blur, frame);
    }

    internal static SKBitmap Render(SKCodec codec, PixelSize size, int blur, int frame)
    {
        size = WallpaperBudget.Clamp(size, false);
        var info = codec.Info;
        var origin = codec.EncodedOrigin;
        var (srcW, srcH) = Oriented(info.Width, info.Height, origin);
        var scale = Math.Max(size.Width / (float)srcW, size.Height / (float)srcH);
        var decode = codec.GetScaledDimensions(Math.Min(1f, scale));
        var decodedInfo = new SKImageInfo(decode.Width, decode.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodedInfo);
        var result = codec.GetPixels(decodedInfo, decoded.GetPixels(), new SKCodecOptions(Math.Clamp(frame, 0, Math.Max(0, codec.FrameCount - 1))));
        if (result is not SKCodecResult.Success and not SKCodecResult.IncompleteInput)
            throw new InvalidOperationException("decode");
        using var oriented = Orient(decoded, origin);
        var output = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Black);
        var cover = Math.Max(size.Width / (float)oriented.Width, size.Height / (float)oriented.Height);
        var dest = SKRect.Create((size.Width - oriented.Width * cover) / 2f, (size.Height - oriented.Height * cover) / 2f,
            oriented.Width * cover, oriented.Height * cover);
        var sigma = Sigma(blur);
        if (sigma > 0)
        {
            var down = sigma > 6 ? 0.35f : sigma > 3 ? 0.5f : 1f;
            if (down < 1f)
            {
                var small = new SKSizeI(Math.Max(1, (int)(size.Width * down)), Math.Max(1, (int)(size.Height * down)));
                using var low = new SKBitmap(new SKImageInfo(small.Width, small.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                using (var lowCanvas = new SKCanvas(low))
                {
                    lowCanvas.Clear(SKColors.Black);
                    lowCanvas.DrawBitmap(oriented, SKRect.Create(dest.Left * down, dest.Top * down, dest.Width * down, dest.Height * down));
                }
                using var blurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma * down, sigma * down) };
                canvas.DrawBitmap(low, SKRect.Create(size.Width, size.Height), blurPaint);
            }
            else
            {
                using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };
                canvas.DrawBitmap(oriented, dest, paint);
            }
        }
        else canvas.DrawBitmap(oriented, dest);
        return output;
    }

    private static float Sigma(int blur) => Math.Clamp(blur, 0, 100) / 100f * 28f;

    private static (int Width, int Height) Oriented(int width, int height, SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom
            ? (height, width) : (width, height);

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            return source.Copy() ?? throw new InvalidOperationException("copy");
        var (width, height) = Oriented(source.Width, source.Height, origin);
        var dest = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(dest);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(origin switch
        {
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0
        });
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return dest;
    }
}

internal sealed class WallpaperMotion : IWallpaperPlayback
{
    private readonly FileStream _stream;
    private readonly SKCodec _codec;
    private readonly Action<WriteableBitmap> _present;
    private readonly int[] _delays;
    private readonly DispatcherTimer _timer;
    private WriteableBitmap? _frame;
    private PixelSize _size;
    private int _blur;
    private int _index;
    private bool _paused = true;
    private bool _disposed;

    public WallpaperMotion(FileStream stream, SKCodec codec, PixelSize size, int blur, Action<WriteableBitmap> present)
    {
        _stream = stream;
        _codec = codec;
        _size = WallpaperBudget.Clamp(size, true);
        _blur = blur;
        _present = present;
        _delays = new int[codec.FrameCount];
        var info = codec.FrameInfo;
        for (var i = 0; i < _delays.Length; i++)
        {
            var delay = info is { Length: > 0 } ? info[Math.Min(i, info.Length - 1)].Duration : 100;
            _delays[i] = Math.Max(WallpaperBudget.MinFrameMs, delay <= 0 ? 100 : delay);
        }
        _timer = new DispatcherTimer();
        _timer.Tick += (_, _) => Advance();
    }

    public void Start() => Show(_index);

    public void SetPaused(bool paused)
    {
        if (_disposed) return;
        _paused = paused;
        if (paused) _timer.Stop();
        else
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_delays[_index]);
            if (!_timer.IsEnabled) _timer.Start();
        }
    }

    public void Update(PixelSize size, int blur)
    {
        size = WallpaperBudget.Clamp(size, false);
        if (size == _size && blur == _blur) return;
        _size = size;
        _blur = blur;
        _frame = null;
        Show(_index);
    }

    private void Advance()
    {
        if (_disposed || _paused) return;
        _index = (_index + 1) % _codec.FrameCount;
        Show(_index);
        _timer.Interval = TimeSpan.FromMilliseconds(_delays[_index]);
    }

    private void Show(int frame)
    {
        using var rendered = WallpaperRaster.Render(_codec, _size, _blur, frame);
        if (_frame is null || _frame.PixelSize.Width != rendered.Width || _frame.PixelSize.Height != rendered.Height)
        {
            _frame?.Dispose();
            _frame = WallpaperFrames.Create(new PixelSize(rendered.Width, rendered.Height));
        }
        WallpaperFrames.Copy(rendered.GetPixels(), rendered.Width, rendered.Height, rendered.RowBytes, _frame);
        _present(_frame);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _frame = null;
        _codec.Dispose();
        _stream.Dispose();
    }
}
