using Chat.UI.Resources;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private VisualResourceBudget _visualBudget = VisualResourceBudget.Active;

    public void ApplyVisualBudget(VisualResourceBudget budget)
    {
        var previous = _visualBudget;
        _visualBudget = budget;
        Wallpaper.SetResourceBudget(budget.Suspended, budget.Animate);
        _previews.SetBudget(budget.ThumbnailBytes, budget.Downloads);
        if (budget.Suspended || previous.Animate != budget.Animate) ClearPlaybacks();
        if (!budget.Suspended)
        {
            SyncMessages();
            if (SelectedInstance?.Context.Session is { } session) _ = LoadUserAvatarAsync(session.Me.Id);
        }
    }
}
