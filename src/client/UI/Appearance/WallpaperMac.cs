using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Chat.UI.Appearance;

[SupportedOSPlatform("macos")]
internal sealed class WallpaperMac : IWallpaperPlayback
{
    private readonly string _path;
    private readonly Action<WriteableBitmap> _present;
    private readonly CancellationTokenSource _lifetime = new();
    private WriteableBitmap? _frame;
    private byte[] _pixels = [];
    private PixelSize _size;
    private int _stride;
    private bool _paused;
    private bool _disposed;
    private int _generation;

    private WallpaperMac(string path, PixelSize size, Action<WriteableBitmap> present)
    {
        _path = path;
        _size = size;
        _present = present;
        Start();
    }

    public static IWallpaperPlayback? TryOpen(string path, PixelSize size, Action<WriteableBitmap> present)
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            Native.Ensure();
            if (!Native.CanOpen(path)) return null;
            return new WallpaperMac(path, WallpaperBudget.Clamp(size), present);
        }
        catch { return null; }
    }

    public void SetPaused(bool paused)
    {
        if (_disposed || _paused == paused) return;
        _paused = paused;
        if (!paused) Start();
    }

    public void Update(PixelSize size, int blur)
    {
        size = WallpaperBudget.Clamp(size);
        if (_disposed || size == _size) return;
        _size = size;
        _frame = null;
        if (!_paused) Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _frame = null;
        _lifetime.Dispose();
    }

    private void Start()
    {
        var generation = ++_generation;
        var size = _size;
        var token = _lifetime.Token;
        _ = Task.Run(() => Run(generation, size, token), token);
    }

    private void Run(int generation, PixelSize size, CancellationToken token)
    {
        try
        {
            while (!_disposed && !_paused && generation == _generation && !token.IsCancellationRequested)
            {
                if (!Native.ReadLoop(_path, size, token, (pixels, width, height, stride) =>
                {
                    if (_disposed || _paused || generation != _generation) return false;
                    var needed = stride * height;
                    if (_pixels.Length < needed) _pixels = new byte[needed];
                    Marshal.Copy(pixels, _pixels, 0, needed);
                    _stride = stride;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_disposed || generation != _generation) return;
                        _frame ??= WallpaperFrames.Create(new PixelSize(width, height));
                        if (_frame.PixelSize.Width != width || _frame.PixelSize.Height != height)
                        {
                            _frame.Dispose();
                            _frame = WallpaperFrames.Create(new PixelSize(width, height));
                        }
                        WallpaperFrames.Copy(_pixels, width, height, _stride, _frame);
                        _present(_frame);
                    }, DispatcherPriority.Render);
                    return !_paused && generation == _generation;
                }))
                    break;
            }
        }
        catch { /* fall back is owned by the session */ }
    }

    private static class Native
    {
        private const int RtldNow = 2;
        private static bool _ready;
        private static IntPtr _avMediaTypeVideo;
        private static IntPtr _pixelFormatKey;
        private static IntPtr _pixelWidthKey;
        private static IntPtr _pixelHeightKey;

        public static void Ensure()
        {
            if (_ready) return;
            Load("/System/Library/Frameworks/Foundation.framework/Foundation");
            Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
            Load("/System/Library/Frameworks/CoreMedia.framework/CoreMedia");
            Load("/System/Library/Frameworks/CoreVideo.framework/CoreVideo");
            var video = Load("/System/Library/Frameworks/AVFoundation.framework/AVFoundation");
            var cv = Load("/System/Library/Frameworks/CoreVideo.framework/CoreVideo");
            _avMediaTypeVideo = Marshal.ReadIntPtr(Sym(video, "AVMediaTypeVideo"));
            _pixelFormatKey = Marshal.ReadIntPtr(Sym(cv, "kCVPixelBufferPixelFormatTypeKey"));
            _pixelWidthKey = Marshal.ReadIntPtr(Sym(cv, "kCVPixelBufferWidthKey"));
            _pixelHeightKey = Marshal.ReadIntPtr(Sym(cv, "kCVPixelBufferHeightKey"));
            _ready = true;
        }

        public static bool CanOpen(string path)
        {
            var pool = Msg(Alloc("NSAutoreleasePool"), Sel("init"));
            try
            {
                var asset = Asset(path);
                return asset != IntPtr.Zero && Track(asset) != IntPtr.Zero;
            }
            finally { Msg(pool, Sel("drain")); }
        }

        public static bool ReadLoop(string path, PixelSize size, CancellationToken token, Func<IntPtr, int, int, int, bool> frame)
        {
            var pool = Msg(Alloc("NSAutoreleasePool"), Sel("init"));
            try
            {
                var asset = Asset(path);
                if (asset == IntPtr.Zero) return false;
                var track = Track(asset);
                if (track == IntPtr.Zero) return false;
                var settings = Settings(size);
                var output = Msg(Msg(Alloc("AVAssetReaderTrackOutput"), Sel("initWithTrack:outputSettings:"), track, settings), Sel("retain"));
                if (output == IntPtr.Zero) return false;
                var error = IntPtr.Zero;
                var reader = InitReader(Msg(GetClass("AVAssetReader"), Sel("alloc")), Sel("initWithAsset:error:"), asset, ref error);
                if (reader == IntPtr.Zero) return false;
                Msg(reader, Sel("addOutput:"), output);
                if (Msg(reader, Sel("startReading")) == IntPtr.Zero) return false;
                var interval = TimeSpan.FromMilliseconds(WallpaperBudget.MinFrameMs);
                while (!token.IsCancellationRequested)
                {
                    var inner = Msg(Alloc("NSAutoreleasePool"), Sel("init"));
                    try
                    {
                        var sample = Msg(output, Sel("copyNextSampleBuffer"));
                        if (sample == IntPtr.Zero) return true;
                        var buffer = CMSampleBufferGetImageBuffer(sample);
                        if (buffer != IntPtr.Zero)
                        {
                            CVPixelBufferLockBaseAddress(buffer, 1);
                            try
                            {
                                var width = (int)CVPixelBufferGetWidth(buffer);
                                var height = (int)CVPixelBufferGetHeight(buffer);
                                var stride = (int)CVPixelBufferGetBytesPerRow(buffer);
                                var pixels = CVPixelBufferGetBaseAddress(buffer);
                                if (pixels != IntPtr.Zero && !frame(pixels, width, height, stride))
                                {
                                    CFRelease(sample);
                                    return false;
                                }
                            }
                            finally { CVPixelBufferUnlockBaseAddress(buffer, 1); }
                        }
                        CFRelease(sample);
                    }
                    finally { Msg(inner, Sel("drain")); }
                    Thread.Sleep(interval);
                }
                return false;
            }
            finally { Msg(pool, Sel("drain")); }
        }

        private static IntPtr Asset(string path)
        {
            var ns = Ns(path);
            var url = Msg(GetClass("NSURL"), Sel("fileURLWithPath:"), ns);
            return Msg(GetClass("AVURLAsset"), Sel("assetWithURL:"), url);
        }

        private static IntPtr Track(IntPtr asset)
        {
            var tracks = Msg(asset, Sel("tracksWithMediaType:"), _avMediaTypeVideo);
            if (tracks == IntPtr.Zero || (long)Msg(tracks, Sel("count")) <= 0) return IntPtr.Zero;
            return Msg(tracks, Sel("objectAtIndex:"), IntPtr.Zero);
        }

        private static IntPtr Settings(PixelSize size)
        {
            var dict = Msg(Alloc("NSMutableDictionary"), Sel("init"));
            Msg(dict, Sel("setObject:forKey:"), Number(0x42475241), _pixelFormatKey);
            Msg(dict, Sel("setObject:forKey:"), Number((uint)size.Width), _pixelWidthKey);
            Msg(dict, Sel("setObject:forKey:"), Number((uint)size.Height), _pixelHeightKey);
            return dict;
        }

        private static IntPtr Number(uint value) => MsgU32(GetClass("NSNumber"), Sel("numberWithUnsignedInt:"), value);
        private static IntPtr Ns(string value)
        {
            var utf8 = Marshal.StringToCoTaskMemUTF8(value);
            try { return MsgStr(GetClass("NSString"), Sel("stringWithUTF8String:"), utf8); }
            finally { Marshal.FreeCoTaskMem(utf8); }
        }
        private static IntPtr Alloc(string name) => Msg(GetClass(name), Sel("alloc"));
        private static IntPtr Load(string path)
        {
            var handle = dlopen(path, RtldNow);
            return handle == IntPtr.Zero ? throw new InvalidOperationException(path) : handle;
        }
        private static IntPtr Sym(IntPtr handle, string name)
        {
            var symbol = dlsym(handle, name);
            return symbol == IntPtr.Zero ? throw new InvalidOperationException(name) : symbol;
        }

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")] private static extern IntPtr GetClass(string name);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern IntPtr Sel(string name);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr recv, IntPtr sel);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr recv, IntPtr sel, IntPtr arg);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr recv, IntPtr sel, IntPtr arg1, IntPtr arg2);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr MsgStr(IntPtr recv, IntPtr sel, IntPtr utf8);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr MsgU32(IntPtr recv, IntPtr sel, uint value);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr InitReader(IntPtr recv, IntPtr sel, IntPtr asset, ref IntPtr error);
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern IntPtr dlopen(string path, int mode);
        [DllImport("/usr/lib/libSystem.B.dylib")] private static extern IntPtr dlsym(IntPtr handle, string name);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr handle);
        [DllImport("/System/Library/Frameworks/CoreMedia.framework/CoreMedia")] private static extern IntPtr CMSampleBufferGetImageBuffer(IntPtr sample);
        [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")] private static extern int CVPixelBufferLockBaseAddress(IntPtr buffer, uint flags);
        [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")] private static extern int CVPixelBufferUnlockBaseAddress(IntPtr buffer, uint flags);
        [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")] private static extern IntPtr CVPixelBufferGetBaseAddress(IntPtr buffer);
        [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")] private static extern nint CVPixelBufferGetBytesPerRow(IntPtr buffer);
        [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")] private static extern nint CVPixelBufferGetWidth(IntPtr buffer);
        [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")] private static extern nint CVPixelBufferGetHeight(IntPtr buffer);
    }
}
