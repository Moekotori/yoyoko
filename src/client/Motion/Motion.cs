using Avalonia;

namespace Chat.Motion;

/// <summary>Window/subtree policy. No global registry, timer or business dependencies.</summary>
public sealed class Motion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> ReduceMotionProperty =
        AvaloniaProperty.RegisterAttached<Motion, StyledElement, bool>("ReduceMotion", inherits: true);

    public static bool GetReduceMotion(StyledElement element) => element.GetValue(ReduceMotionProperty);
    public static void SetReduceMotion(StyledElement element, bool value) => element.SetValue(ReduceMotionProperty, value);
}

public enum MotionPreset { None, Fade, Enter, SlideLeft, SlideRight }

/// <summary>Short, finite durations shared by page motion and control feedback.</summary>
public static class MotionTokens
{
    public static TimeSpan Feedback { get; } = TimeSpan.FromMilliseconds(120);
    public static TimeSpan Page { get; } = TimeSpan.FromMilliseconds(160);
    public const double Distance = 8;
}
