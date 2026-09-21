namespace Chat.UI.Resources;

public sealed record VisualResourceBudget(long ThumbnailBytes, int Downloads, bool Animate, bool Suspended)
{
    public long AvatarBytes => Suspended ? 0 : Animate ? 8 * 1024 * 1024 : 1024 * 1024;
    public static readonly VisualResourceBudget Active = new(16 * 1024 * 1024, 4, true, false);
    public static readonly VisualResourceBudget Idle = new(4 * 1024 * 1024, 1, false, false);
    public static readonly VisualResourceBudget Pressure = new(2 * 1024 * 1024, 1, false, false);
    public static readonly VisualResourceBudget Hidden = new(0, 0, false, true);
}

// RSS is a process-residency guard, not managed heap size or an OS pressure notification.
public sealed class VisualResourcePolicy
{
    private bool _pressure;
    private TimeSpan? _recoverySince;

    public void Sample(TimeSpan now, long workingSetBytes, double systemLoad)
    {
        if (workingSetBytes >= 256L * 1024 * 1024 || systemLoad >= 0.90)
        {
            _pressure = true;
            _recoverySince = null;
        }
        else if (_pressure && workingSetBytes < 224L * 1024 * 1024 && systemLoad < 0.80)
        {
            _recoverySince ??= now;
            if (now - _recoverySince.Value >= TimeSpan.FromSeconds(30)) _pressure = false;
        }
        else _recoverySince = null;
    }

    public VisualResourceBudget Resolve(bool visible, bool active, TimeSpan idle) =>
        !visible ? VisualResourceBudget.Hidden : _pressure ? VisualResourceBudget.Pressure
        : !active || idle >= TimeSpan.FromSeconds(60) ? VisualResourceBudget.Idle : VisualResourceBudget.Active;
}
