using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Chat.Motion;

/// <summary>
/// Finite wheel easing for one ScrollViewer. No resident clock; a new notch retargets the same run.
/// </summary>
public sealed class SmoothScroll
{
    private SmoothScroll() { }

    public const double Line = 50;

    public static readonly AttachedProperty<bool> WheelProperty =
        AvaloniaProperty.RegisterAttached<SmoothScroll, Control, bool>("Wheel");

    private static readonly AttachedProperty<double> TargetProperty =
        AvaloniaProperty.RegisterAttached<SmoothScroll, AvaloniaObject, double>("Target");
    private static readonly AttachedProperty<CancellationTokenSource?> RunProperty =
        AvaloniaProperty.RegisterAttached<SmoothScroll, AvaloniaObject, CancellationTokenSource?>("Run");

    static SmoothScroll()
    {
        WheelProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.GetNewValue<bool>())
                control.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
            else
                control.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        });
    }

    public static bool GetWheel(Control control) => control.GetValue(WheelProperty);
    public static void SetWheel(Control control, bool value) => control.SetValue(WheelProperty, value);

    public static bool IsRunning(ScrollViewer scroll) => scroll.GetValue(RunProperty) is not null;

    public static void Sync(ScrollViewer scroll)
    {
        if (!IsRunning(scroll)) scroll.SetValue(TargetProperty, scroll.Offset.Y);
    }

    public static void Cancel(ScrollViewer scroll)
    {
        var current = scroll.Offset;
        Stop(scroll);
        scroll.Offset = current;
        scroll.SetValue(TargetProperty, current.Y);
    }

    public static void Jump(ScrollViewer scroll, double y)
    {
        Stop(scroll);
        var next = Clamp(scroll, y);
        scroll.Offset = new Vector(scroll.Offset.X, next);
        scroll.SetValue(TargetProperty, next);
    }

    public static void RestoreAfterPrepend(ScrollViewer scroll, double extentBefore, double offsetBefore)
    {
        Jump(scroll, offsetBefore + scroll.Extent.Height - extentBefore);
    }

    public static void To(ScrollViewer scroll, double y)
    {
        if (Motion.GetReduceMotion(scroll) || !scroll.IsEffectivelyVisible)
        {
            Jump(scroll, y);
            return;
        }
        var target = Clamp(scroll, y);
        scroll.SetValue(TargetProperty, target);
        var from = scroll.Offset;
        if (Math.Abs(from.Y - target) < 0.5)
        {
            scroll.Offset = new Vector(from.X, target);
            return;
        }
        Stop(scroll);
        var lifetime = new CancellationTokenSource();
        scroll.SetValue(RunProperty, lifetime);
        _ = RunAsync(scroll, from, new Vector(from.X, target), lifetime);
    }

    public static void Wheel(ScrollViewer scroll, double deltaY)
    {
        var origin = IsRunning(scroll) ? scroll.GetValue(TargetProperty) : scroll.Offset.Y;
        To(scroll, origin + deltaY);
    }

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not Control control || e.Handled || Math.Abs(e.Delta.Y) < 0.0001) return;
        var scroll = control as ScrollViewer ?? control.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null || scroll.Extent.Height <= scroll.Viewport.Height) return;
        Wheel(scroll, -e.Delta.Y * Line);
        e.Handled = true;
    }

    private static async Task RunAsync(ScrollViewer scroll, Vector from, Vector to, CancellationTokenSource lifetime)
    {
        var animation = new Animation
        {
            Duration = MotionTokens.Page,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                Frame(0, from),
                Frame(1, to)
            }
        };
        try
        {
            await animation.RunAsync(scroll, lifetime.Token);
            if (!lifetime.IsCancellationRequested) scroll.Offset = to;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(scroll.GetValue(RunProperty), lifetime))
                scroll.SetValue(RunProperty, null);
            lifetime.Dispose();
        }
    }

    private static void Stop(ScrollViewer scroll)
    {
        var previous = scroll.GetValue(RunProperty);
        scroll.SetValue(RunProperty, null);
        previous?.Cancel();
    }

    private static double Clamp(ScrollViewer scroll, double y)
    {
        var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        return Math.Clamp(y, 0, max);
    }

    private static KeyFrame Frame(double cue, Vector offset) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(ScrollViewer.OffsetProperty, offset) }
    };
}
