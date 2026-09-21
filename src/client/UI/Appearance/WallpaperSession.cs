using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Chat.Core;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Settings;

namespace Chat.UI.Appearance;

public sealed class WallpaperSession : ObservableObject, IDisposable
{
    private readonly IAppearancePreference _appearance;
    private readonly IChatChrome _chrome;
    private readonly I18n _text;
    private Bitmap? _frame;
    private IWallpaperPlayback? _playback;
    private PixelSize _viewport;
    private string _error = "";
    private bool _paused = true;
    private bool _resourceSuspended;
    private bool _resourceAnimationPaused;
    private bool _disposed;
    private int _load;
    private int _appliedBlur = int.MinValue;
    private bool _appliedEnabled;
    private string _appliedFile = "";
    private DispatcherTimer? _resize;
    private DispatcherTimer? _blur;

    public WallpaperSession(IAppearancePreference appearance, IChatChrome chrome, I18n text)
    {
        _appearance = appearance;
        _chrome = chrome;
        _text = text;
        Choose = new(ChooseAsync, OnError);
        Clear = new(_ => Remove());
        _appearance.Changed += OnAppearance;
        _chrome.Changed += OnChrome;
        Notify();
    }

    public Func<Task<PickedFile?>>? PickFile { get; set; }
    public AsyncCommand Choose { get; }
    public ActionCommand Clear { get; }
    public Bitmap? Frame { get => _frame; private set { if (ReferenceEquals(_frame, value)) return; _frame = value; Changed(); Changed(nameof(IsActive)); } }
    public bool IsActive => _appearance.WallpaperEnabled && Frame is not null;
    public bool HasFile => _appearance.WallpaperFile.Length > 0;
    public bool CanAdjust => HasFile;
    public string FileLabel => _appearance.WallpaperLabel;
    public bool HasLabel => FileLabel.Length > 0;
    public string Error { get => _error; private set { if (_error == value) return; _error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public bool Enabled { get => _appearance.WallpaperEnabled; set => _appearance.SetWallpaperEnabled(value); }
    public double Blur
    {
        get => _appearance.WallpaperBlur;
        set => _appearance.SetWallpaperBlur((int)Math.Round(value));
    }
    public double Brightness
    {
        get => _appearance.WallpaperBrightness;
        set => _appearance.SetWallpaperBrightness((int)Math.Round(value));
    }
    public double DimOpacity => Math.Clamp(1 - _appearance.WallpaperBrightness / 100d, 0, 0.92);
    public double LiveBlur => IsVideo ? _appearance.WallpaperBlur / 100d * 24 : 0;
    public bool IsVideo { get; private set; }

    public void SetViewport(PixelSize size)
    {
        size = WallpaperBudget.Clamp(size);
        if (size.Width < 16 || size.Height < 16 || size == _viewport) return;
        _viewport = size;
        _resize ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _resize.Tick -= OnResize;
        _resize.Tick += OnResize;
        _resize.Stop();
        _resize.Start();
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        _playback?.SetPaused(paused || _resourceAnimationPaused || _chrome.ReduceMotion);
    }

    public void SetResourceBudget(bool suspended, bool animate)
    {
        var wasSuspended = _resourceSuspended;
        _resourceSuspended = suspended;
        _resourceAnimationPaused = !animate;
        if (suspended)
        {
            ++_load; // Any already-decoding frame is stale on delivery.
            _resize?.Stop();
            _blur?.Stop();
            var ownedFrame = _playback is null ? _frame : null;
            Frame = null;
            StopPlayback();
            ownedFrame?.Dispose();
        }
        else if (wasSuspended) Reload();
        else OnChrome();
    }

    public async Task ImportAsync(PickedFile file)
    {
        var extension = WallpaperBudget.NormalizeExtension(file.FileName)
            ?? throw new ClientFault(TextKey.InvalidWallpaper);
        var limit = WallpaperBudget.IsVideo(extension) ? WallpaperBudget.MaxVideoBytes : WallpaperBudget.MaxImageBytes;
        if (file.Size <= 0 || file.Size > limit) throw new ClientFault(TextKey.WallpaperTooLarge);
        Directory.CreateDirectory(_appearance.WallpaperDirectory);
        var stored = "current" + extension;
        var path = Path.Combine(_appearance.WallpaperDirectory, stored);
        var temp = path + ".tmp";
        await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
        {
            await file.Content.CopyToAsync(output);
            if (output.Length > limit)
            {
                output.Dispose();
                File.Delete(temp);
                throw new ClientFault(TextKey.WallpaperTooLarge);
            }
        }
        foreach (var previous in Directory.GetFiles(_appearance.WallpaperDirectory, "current.*"))
            if (!string.Equals(previous, path, StringComparison.OrdinalIgnoreCase))
                TryDelete(previous);
        File.Move(temp, path, true);
        Error = "";
        _appearance.SetWallpaperFile(stored, Path.GetFileName(file.FileName));
        _appearance.SetWallpaperEnabled(true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _appearance.Changed -= OnAppearance;
        _chrome.Changed -= OnChrome;
        _resize?.Stop();
        _blur?.Stop();
        StopPlayback();
        _frame?.Dispose();
        _frame = null;
        _appearance.Flush();
    }

    private void Remove()
    {
        StopPlayback();
        Frame = null;
        foreach (var previous in Directory.GetFiles(_appearance.WallpaperDirectory, "current.*"))
            TryDelete(previous);
        _appearance.SetWallpaperFile("", "");
        _appearance.SetWallpaperEnabled(false);
        Error = "";
        IsVideo = false;
        Changed(nameof(IsVideo));
        Changed(nameof(LiveBlur));
    }

    private async Task ChooseAsync()
    {
        if (PickFile is null) return;
        var file = await PickFile();
        if (file is null) return;
        await using (file) await ImportAsync(file);
    }

    private void OnAppearance()
    {
        Notify();
        if (_appliedBlur == _appearance.WallpaperBlur && _appliedEnabled == _appearance.WallpaperEnabled &&
            _appliedFile == _appearance.WallpaperFile)
            return;
        _blur ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _blur.Tick -= OnBlur;
        _blur.Tick += OnBlur;
        _blur.Stop();
        _blur.Start();
    }

    private void OnResize(object? sender, EventArgs e)
    {
        _resize?.Stop();
        Reload();
    }

    private void OnBlur(object? sender, EventArgs e)
    {
        _blur?.Stop();
        Reload();
    }

    private void OnChrome() => _playback?.SetPaused(_paused || _resourceAnimationPaused || _chrome.ReduceMotion);

    private void Reload()
    {
        if (_disposed || _resourceSuspended) return;
        var id = ++_load;
        var enabled = _appearance.WallpaperEnabled;
        var relative = _appearance.WallpaperFile;
        var blur = _appearance.WallpaperBlur;
        var viewport = _viewport;
        _appliedBlur = blur;
        _appliedEnabled = enabled;
        _appliedFile = relative;
        if (!enabled || relative.Length == 0 || viewport.Width < 16)
        {
            StopPlayback();
            if (!enabled) Frame = null;
            Notify();
            return;
        }
        var path = Path.Combine(_appearance.WallpaperDirectory, Path.GetFileName(relative));
        if (!File.Exists(path))
        {
            StopPlayback();
            Frame = null;
            Error = _text.Get(TextKey.InvalidWallpaper);
            Notify();
            return;
        }
        var extension = WallpaperBudget.NormalizeExtension(path) ?? "";
        _ = Task.Run(() => Load(id, path, extension, viewport, blur));
    }

    private void Load(int id, string path, string extension, PixelSize size, int blur)
    {
        try
        {
            if (WallpaperBudget.IsVideo(extension))
            {
                Dispatcher.UIThread.Post(() => StartVideo(id, path, size));
                return;
            }
            if (IsAnimated(path))
            {
                Dispatcher.UIThread.Post(() => StartMotion(id, path, size, blur));
                return;
            }
            var bitmap = WallpaperRaster.Decode(path, size, blur);
            Dispatcher.UIThread.Post(() =>
            {
                if (id != _load) { bitmap.Dispose(); return; }
                StopPlayback();
                IsVideo = false;
                Present(bitmap);
                Error = "";
                Notify();
            });
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (id != _load) return;
                StopPlayback();
                Frame = null;
                OnError(exception);
            });
        }
    }

    private static bool IsAnimated(string path)
    {
        using var stream = File.OpenRead(path);
        using var codec = SkiaSharp.SKCodec.Create(stream);
        return codec is { FrameCount: > 1 };
    }

    private void StartMotion(int id, string path, PixelSize size, int blur)
    {
        if (id != _load) return;
        var motion = WallpaperRaster.TryMotion(path, size, blur, frame =>
        {
            if (id != _load) return;
            Present(frame);
        });
        if (motion is null)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var bitmap = WallpaperRaster.Decode(path, size, blur);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (id != _load) { bitmap.Dispose(); return; }
                        StopPlayback();
                        IsVideo = false;
                        Present(bitmap);
                        Error = "";
                        Notify();
                    });
                }
                catch (Exception exception)
                {
                    Dispatcher.UIThread.Post(() => { if (id == _load) OnError(exception); });
                }
            });
            return;
        }
        StopPlayback();
        IsVideo = false;
        _playback = motion;
        motion.Start();
        motion.SetPaused(_paused || _resourceAnimationPaused || _chrome.ReduceMotion);
        Error = "";
        Notify();
    }

    private void StartVideo(int id, string path, PixelSize size)
    {
        if (id != _load) return;
        StopPlayback();
        IsVideo = true;
        var playback = WallpaperVideo.TryOpen(path, size, frame =>
        {
            if (id != _load) return;
            Present(frame);
        }, message =>
        {
            if (id != _load) return;
            Error = string.IsNullOrWhiteSpace(message) ? _text.Get(TextKey.WallpaperVideoUnavailable) : message;
        });
        if (playback is null)
        {
            Frame = null;
            Error = _text.Get(TextKey.WallpaperVideoUnavailable);
            Notify();
            return;
        }
        _playback = playback;
        playback.SetPaused(_paused || _resourceAnimationPaused || _chrome.ReduceMotion);
        Error = "";
        Notify();
    }

    private void Present(Bitmap frame)
    {
        var previous = _frame;
        Frame = frame;
        if (previous is not null && !ReferenceEquals(previous, frame))
            previous.Dispose();
        Changed(nameof(IsActive));
    }

    private void StopPlayback()
    {
        _playback?.Dispose();
        _playback = null;
    }

    private void Notify()
    {
        Changed(nameof(Enabled));
        Changed(nameof(HasFile));
        Changed(nameof(CanAdjust));
        Changed(nameof(FileLabel));
        Changed(nameof(HasLabel));
        Changed(nameof(Blur));
        Changed(nameof(Brightness));
        Changed(nameof(DimOpacity));
        Changed(nameof(LiveBlur));
        Changed(nameof(IsVideo));
        Changed(nameof(IsActive));
    }

    private void OnError(Exception exception) => Error = _text.Error(exception);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* keep going */ }
    }
}
