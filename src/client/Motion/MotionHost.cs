using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Chat.Motion;

/// <summary>
/// Wrap a page/panel once. Visibility and Trigger changes play a finite entry transition.
/// Owns its Opacity/RenderTransform; transforms on Child remain untouched. UI-thread only.
/// </summary>
public sealed class MotionHost : Decorator
{
    public static readonly StyledProperty<object?> TriggerProperty =
        AvaloniaProperty.Register<MotionHost, object?>(nameof(Trigger));
    public static readonly StyledProperty<MotionPreset> PresetProperty =
        AvaloniaProperty.Register<MotionHost, MotionPreset>(nameof(Preset), MotionPreset.Enter);
    public static readonly StyledProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.Register<MotionHost, TimeSpan>(nameof(Duration), MotionTokens.Page,
            validate: value => value >= TimeSpan.Zero && value <= TimeSpan.FromMilliseconds(500));

    private readonly TranslateTransform _translation = new();
    private CancellationTokenSource? _running;
    private DispatcherOperation? _pending;
    private Window? _window;
    private Visual[] _ancestors = [];
    private bool _attached;

    public MotionHost()
    {
        RenderTransform = _translation;
    }

    public object? Trigger { get => GetValue(TriggerProperty); set => SetValue(TriggerProperty, value); }
    public MotionPreset Preset { get => GetValue(PresetProperty); set => SetValue(PresetProperty, value); }
    public TimeSpan Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public bool IsRunning => _running is not null;

    /// <summary>Coalesces requests in the same UI turn; an active transition continues from its current values.</summary>
    public void Play()
    {
        VerifyAccess();
        if (!CanAnimate) { Stop(); return; }
        if (_pending is not null) return;
        _pending = Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pending = null;
            if (!CanAnimate) { Stop(); return; }
            var interrupted = IsRunning;
            var opacity = interrupted ? Opacity : 0;
            var x = interrupted ? _translation.X : Preset switch
            {
                MotionPreset.SlideLeft => MotionTokens.Distance,
                MotionPreset.SlideRight => -MotionTokens.Distance,
                _ => 0
            };
            var y = interrupted ? _translation.Y : Preset == MotionPreset.Enter ? MotionTokens.Distance : 0;
            Stop();
            var lifetime = new CancellationTokenSource();
            _running = lifetime;
            _ = RunAsync(lifetime, opacity, x, y);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Cancel immediately and reveal the final state, including any pending request.</summary>
    public void Stop()
    {
        VerifyAccess();
        _pending?.Abort();
        _pending = null;
        var previous = _running;
        _running = null;
        previous?.Cancel();
        // Disposal belongs to RunAsync, even when a newer transition has replaced this one.
    }

    private bool CanAnimate => _attached && IsEffectivelyVisible && Child is not null &&
        !Motion.GetReduceMotion(this) && Preset != MotionPreset.None && Duration > TimeSpan.Zero &&
        (_window is null || (_window.IsVisible && _window.WindowState != WindowState.Minimized));

    private async Task RunAsync(CancellationTokenSource lifetime, double opacity, double x, double y)
    {
        try
        {
            await MotionAnimation.Create(Duration, opacity, x, y).RunAsync(this, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_running, lifetime)) _running = null;
            lifetime.Dispose();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        // Effective-visibility notifications are internal to Avalonia. Observe only this
        // host's ancestor chain, and release every subscription on detach.
        _ancestors = this.GetVisualAncestors().ToArray();
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged += OnAncestorChanged;
        Play();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Stop();
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= OnAncestorChanged;
        _ancestors = [];
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TriggerProperty || change.Property == ChildProperty) Play();
        else if (change.Property == IsVisibleProperty)
        {
            if (IsVisible) Play();
            else Stop();
        }
        else if (change.Property == Motion.ReduceMotionProperty || change.Property == PresetProperty ||
                 change.Property == DurationProperty)
            Stop(); // A preference change never introduces motion by itself.
    }

    private void OnAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            if (sender is Visual { IsVisible: true }) Play();
            else Stop();
        }
        else if (e.Property == Window.WindowStateProperty && !CanAnimate) Stop();
    }
}
