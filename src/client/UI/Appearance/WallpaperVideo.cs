using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Chat.UI.Appearance;

internal interface IWallpaperPlayback : IDisposable
{
    void SetPaused(bool paused);
    void Update(PixelSize size, int blur);
}

internal static class WallpaperVideo
{
    public static IWallpaperPlayback? TryOpen(string path, PixelSize size, Action<WriteableBitmap> present, Action<string> failed)
    {
        size = WallpaperBudget.Clamp(size);
        if (OperatingSystem.IsMacOS() && WallpaperMac.TryOpen(path, size, present) is { } mac)
            return mac;
        if (WallpaperFfmpeg.TryOpen(path, size, present, failed) is { } ffmpeg)
            return ffmpeg;
        return null;
    }
}

internal sealed class WallpaperFfmpeg : IWallpaperPlayback
{
    private readonly string _path;
    private readonly Action<WriteableBitmap> _present;
    private readonly Action<string> _failed;
    private readonly object _gate = new();
    private readonly byte[] _read = new byte[64 * 1024];
    private Process? _process;
    private WriteableBitmap? _frame;
    private byte[] _pixels = [];
    private byte[] _jpeg = new byte[256 * 1024];
    private PixelSize _size;
    private int _jpegLength;
    private bool _paused;
    private bool _disposed;
    private int _generation;

    private WallpaperFfmpeg(string path, PixelSize size, Action<WriteableBitmap> present, Action<string> failed)
    {
        _path = path;
        _size = size;
        _present = present;
        _failed = failed;
        Start();
    }

    public static WallpaperFfmpeg? TryOpen(string path, PixelSize size, Action<WriteableBitmap> present, Action<string> failed)
    {
        if (Find() is null) return null;
        return new(path, WallpaperBudget.Clamp(size), present, failed);
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_disposed || _paused == paused) return;
            _paused = paused;
            if (paused) StopProcess();
            else Start();
        }
    }

    public void Update(PixelSize size, int blur)
    {
        size = WallpaperBudget.Clamp(size);
        lock (_gate)
        {
            if (_disposed || size == _size) return;
            _size = size;
            _frame?.Dispose();
            _frame = null;
            if (!_paused) Start();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopProcess();
            _frame?.Dispose();
        }
    }

    private void Start()
    {
        StopProcess();
        var ffmpeg = Find();
        if (ffmpeg is null) return;
        var generation = ++_generation;
        var size = _size;
        var start = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        Add(start, "-hide_banner", "-loglevel", "error", "-nostdin");
        if (OperatingSystem.IsMacOS()) Add(start, "-hwaccel", "videotoolbox");
        Add(start, "-stream_loop", "-1", "-i", _path, "-an",
            "-vf", $"scale={size.Width}:{size.Height}:force_original_aspect_ratio=increase:flags=fast_bilinear,crop={size.Width}:{size.Height},fps={WallpaperBudget.MaxFps}",
            "-f", "mjpeg", "-q:v", "6", "pipe:1");
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg");
            process.StandardInput.Close();
        }
        catch (Exception exception)
        {
            _failed(exception.Message);
            return;
        }
        _process = process;
        _ = Drain(process.StandardError);
        _ = Task.Run(() => Read(process, generation, size));
    }

    private async Task Read(Process process, int generation, PixelSize size)
    {
        try
        {
            var output = process.StandardOutput.BaseStream;
            _jpegLength = 0;
            while (!_disposed && generation == _generation && !process.HasExited)
            {
                var read = await output.ReadAsync(_read);
                if (read <= 0) break;
                Append(read);
                while (TryNextJpeg(out var jpeg, out var length))
                    Present(jpeg, length, size, generation);
            }
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
                Dispatcher.UIThread.Post(() => _failed(exception.Message));
        }
    }

    private void Append(int read)
    {
        if (_jpegLength + read > WallpaperBudget.MaxJpegFrameBytes)
            _jpegLength = 0;
        if (_jpegLength + read > _jpeg.Length)
            Array.Resize(ref _jpeg, Math.Min(WallpaperBudget.MaxJpegFrameBytes, Math.Max(_jpeg.Length * 2, _jpegLength + read)));
        Buffer.BlockCopy(_read, 0, _jpeg, _jpegLength, read);
        _jpegLength += read;
    }

    private bool TryNextJpeg(out byte[] buffer, out int length)
    {
        buffer = _jpeg;
        length = 0;
        var start = IndexOf(_jpeg, _jpegLength, 0xFF, 0xD8, 0);
        if (start < 0)
        {
            _jpegLength = 0;
            return false;
        }
        var end = IndexOf(_jpeg, _jpegLength, 0xFF, 0xD9, start + 2);
        if (end < 0)
        {
            if (start > 0)
            {
                Buffer.BlockCopy(_jpeg, start, _jpeg, 0, _jpegLength - start);
                _jpegLength -= start;
            }
            return false;
        }
        length = end + 2 - start;
        if (start > 0)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(_jpeg, start, copy, 0, length);
            buffer = copy;
        }
        var remain = _jpegLength - (end + 2);
        if (remain > 0) Buffer.BlockCopy(_jpeg, end + 2, _jpeg, 0, remain);
        _jpegLength = remain;
        return true;
    }

    private void Present(byte[] jpeg, int length, PixelSize size, int generation)
    {
        using var data = SkiaSharp.SKData.CreateCopy(jpeg, 0, length);
        using var codec = SkiaSharp.SKCodec.Create(data);
        if (codec is null) return;
        var info = new SkiaSharp.SKImageInfo(size.Width, size.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        var needed = info.RowBytes * info.Height;
        if (_pixels.Length < needed) _pixels = new byte[needed];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(_pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (codec.GetPixels(info, handle.AddrOfPinnedObject()) is not SkiaSharp.SKCodecResult.Success and not SkiaSharp.SKCodecResult.IncompleteInput)
                return;
        }
        finally { handle.Free(); }
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != _generation) return;
            _frame ??= WallpaperFrames.Create(size);
            if (_frame.PixelSize != size)
            {
                _frame.Dispose();
                _frame = WallpaperFrames.Create(size);
            }
            WallpaperFrames.Copy(_pixels, size.Width, size.Height, info.RowBytes, _frame);
            _present(_frame);
        }, DispatcherPriority.Render);
    }

    private void StopProcess()
    {
        _generation++;
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(true);
        }
        catch { /* already gone */ }
        try { _process.Dispose(); } catch { /* ignore */ }
        _process = null;
    }

    private static async Task Drain(StreamReader reader)
    {
        try { await reader.ReadToEndAsync(); }
        catch { /* process ended */ }
    }

    private static int IndexOf(byte[] data, int length, byte first, byte second, int offset)
    {
        for (var i = offset; i < length - 1; i++)
            if (data[i] == first && data[i + 1] == second) return i;
        return -1;
    }

    private static void Add(ProcessStartInfo start, params string[] arguments)
    {
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
    }

    internal static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("CHAT_FFMPEG");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var unix = Path.Combine(folder, "ffmpeg");
            if (File.Exists(unix)) return unix;
            var windows = Path.Combine(folder, "ffmpeg.exe");
            if (File.Exists(windows)) return windows;
        }
        foreach (var candidate in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg" })
            if (File.Exists(candidate)) return candidate;
        return null;
    }
}
