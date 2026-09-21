using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Chat.Motion;

/// <summary>
/// Accordion reveal. Child stays at full size and is clipped; the host reports
/// an interpolated height so siblings move. Interruptible; first layout snaps.
/// </summary>
public sealed class RevealHost : Decorator
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<RevealHost, bool>(nameof(IsOpen), true);
    public static readonly StyledProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.Register<RevealHost, TimeSpan>(nameof(Duration), MotionTokens.Reveal,
            validate: value => value >= TimeSpan.Zero && value <= TimeSpan.FromMilliseconds(500));
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<RevealHost, double>(nameof(Progress), 1,
            validate: value => double.IsFinite(value) && value >= 0 && value <= 1);

    private readonly RectangleGeometry _clip = new();
    private CancellationTokenSource? _running;
    private DispatcherOperation? _pending;
    private Window? _window;
    private Visual[] _ancestors = [];
    private bool _attached;
    private bool _ready;

    static RevealHost() => AffectsMeasure<RevealHost>(ProgressProperty);

    public RevealHost()
    {
        ClipToBounds = true;
        Clip = _clip;
    }

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public TimeSpan Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); private set => SetValue(ProgressProperty, value); }
    public bool IsRunning => _running is not null;

    public void Stop()
    {
        VerifyAccess();
        Snap();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null) return default;
        Child.Measure(availableSize);
        var progress = Progress;
        var height = progress <= 0 ? 0 : Child.DesiredSize.Height * progress;
        return new Size(Child.DesiredSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null) return finalSize;
        var revealed = Child.DesiredSize.Height * Progress;
        Child.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(Child.DesiredSize.Height, revealed)));
        if (Progress >= 1 && !IsRunning)
        {
            Clip = null;
            UseLayoutRounding = true;
        }
        else
        {
            UseLayoutRounding = false;
            Clip = _clip;
            _clip.Rect = new Rect(0, 0, finalSize.Width, revealed);
        }
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        _ancestors = this.GetVisualAncestors().ToArray();
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged += OnAncestorChanged;
        Snap();
        Dispatcher.UIThread.Post(() => { if (_attached) _ready = true; }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ready = false;
        _attached = false;
        CaptureAndStop();
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= OnAncestorChanged;
        _ancestors = [];
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ProgressProperty)
            Opacity = Progress <= 0 ? 0 : Progress >= 0.12 ? 1 : Progress / 0.12;
        else if (change.Property == IsOpenProperty)
        {
            if (_attached && _ready) Play();
            else Snap();
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (!IsVisible) Snap();
        }
        else if (change.Property == Motion.ReduceMotionProperty || change.Property == DurationProperty)
            Snap();
    }

    private void Play()
    {
        VerifyAccess();
        if (_pending is not null) return;
        _pending = Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pending = null;
            Retarget();
        }, DispatcherPriority.Loaded);
    }

    private void Retarget()
    {
        var target = IsOpen ? 1.0 : 0.0;
        var current = CaptureAndStop();
        if (!CanAnimate)
        {
            Progress = target;
            return;
        }
        var distance = Math.Abs(target - current);
        if (distance < 0.001)
        {
            Progress = target;
            return;
        }
        Progress = current;
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(Duration.TotalMilliseconds * distance, 90, Duration.TotalMilliseconds));
        var lifetime = new CancellationTokenSource();
        _running = lifetime;
        _ = RunAsync(lifetime, current, target, duration);
    }

    private async Task RunAsync(CancellationTokenSource lifetime, double from, double to, TimeSpan duration)
    {
        try
        {
            await Create(duration, from, to).RunAsync(this, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_running, lifetime))
            {
                _running = null;
                if (!lifetime.IsCancellationRequested) Progress = to;
            }
            lifetime.Dispose();
            InvalidateArrange();
        }
    }

    private static Animation Create(TimeSpan duration, double from, double to) => new()
    {
        Duration = duration,
        Easing = MotionTokens.RevealEase,
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0, from),
            Frame(1, to)
        }
    };

    private static KeyFrame Frame(double cue, double progress) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(ProgressProperty, progress) }
    };

    private void Snap()
    {
        CaptureAndStop();
        Progress = IsOpen ? 1 : 0;
        UseLayoutRounding = true;
        InvalidateArrange();
    }

    private double CaptureAndStop()
    {
        var current = Progress;
        _pending?.Abort();
        _pending = null;
        var previous = _running;
        _running = null;
        previous?.Cancel();
        Progress = current;
        return current;
    }

    private bool CanAnimate => _attached && IsEffectivelyVisible && Child is not null &&
        !Motion.GetReduceMotion(this) && Duration > TimeSpan.Zero &&
        (_window is null || (_window.IsVisible && _window.WindowState != WindowState.Minimized));

    private void OnAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            if (sender is Visual { IsVisible: false }) Snap();
        }
        else if (e.Property == Window.WindowStateProperty && !CanAnimate) Snap();
    }
}
