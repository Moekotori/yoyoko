using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Chat.UI.Appearance;

public partial class WallpaperView : UserControl
{
    private WallpaperSession? _session;
    private Window? _window;
    private Bitmap? _frame;

    public WallpaperView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        SizeChanged += OnSize;
        DataContextChanged += (_, _) => Bind(DataContext as WallpaperSession);
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindow;
            Pause(_window.WindowState == WindowState.Minimized);
        }
        ReportSize();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.PropertyChanged -= OnWindow;
        _window = null;
        Pause(true);
    }

    private void OnSize(object? sender, SizeChangedEventArgs e) => ReportSize();

    private void OnWindow(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && _window is not null)
            Pause(_window.WindowState == WindowState.Minimized);
    }

    private void Bind(WallpaperSession? session)
    {
        if (_session is not null) _session.PropertyChanged -= OnSession;
        _session = session;
        if (_session is not null) _session.PropertyChanged += OnSession;
        ReportSize();
        Pause(_window?.WindowState == WindowState.Minimized);
    }

    private void OnSession(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WallpaperSession.Frame) or null or "")
        {
            if (!ReferenceEquals(_frame, _session?.Frame))
                _frame = _session?.Frame;
            Dispatcher.UIThread.Post(() => Surface.InvalidateVisual(), DispatcherPriority.Render);
        }
    }

    private void ReportSize()
    {
        if (_session is null || Bounds.Width < 16 || Bounds.Height < 16) return;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _session.SetViewport(new PixelSize(
            Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling))));
    }

    private void Pause(bool paused) => _session?.SetPaused(paused);
}
