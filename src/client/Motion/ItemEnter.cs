using Avalonia;
using Avalonia.Media;

namespace Chat.Motion;

/// <summary>
/// One-shot enter for a newly inserted list row. Not a per-item host; play once then leave the visual settled.
/// </summary>
public static class ItemEnter
{
    public static void Play(Visual target)
    {
        if (target is not StyledElement element || Motion.GetReduceMotion(element)) return;
        if (!target.IsEffectivelyVisible) return;
        if (target.RenderTransform is not TranslateTransform)
            target.RenderTransform = new TranslateTransform();
        _ = MotionAnimation.Create(MotionTokens.Feedback, 0.55, 0, 6).RunAsync(target);
    }
}
