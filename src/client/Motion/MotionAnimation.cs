using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace Chat.Motion;

internal static class MotionAnimation
{
    internal static Animation Create(TimeSpan duration, double opacity, double x, double y) => new()
    {
        Duration = duration,
        Easing = new CubicEaseOut(),
        // Base values already represent the final state. Completion/cancellation removes animation values.
        FillMode = FillMode.None,
        Children =
        {
            Frame(0, opacity, x, y),
            Frame(1, 1, 0, 0)
        }
    };

    private static KeyFrame Frame(double cue, double opacity, double x, double y) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(Visual.OpacityProperty, opacity),
            new Setter(TranslateTransform.XProperty, x),
            new Setter(TranslateTransform.YProperty, y)
        }
    };
}
