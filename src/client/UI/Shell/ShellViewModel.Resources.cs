using Chat.UI.Resources;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private VisualResourceBudget _visualBudget = VisualResourceBudget.Active;
    public bool UltraLightEnabled => _chrome.UltraLightEnabled;
    public bool IsUltraLightParked { get; private set; }
    private bool CanObserveTimeline => !_visualBudget.Suspended && !IsUltraLightParked && ShowChat;
    public (Guid Channel, double Offset, bool AtBottom)? TimelineViewport { get; set; }

    public void SetUltraLightParked(bool parked)
    {
        IsUltraLightParked = parked;
        Settings.Connection.SetWatching(!parked && ShowSettings && Settings.ShowConnection);
        if (parked)
        {
            // The Core timeline (including uploads), session and voice survive this projection trim.
            CloseJump();
            Settings.Shortcuts.CancelCapture();
            _typingTimer?.Stop();
            Messages.Clear();
            VisibleMessages.Clear();
            Participants.Clear();
        }
        else
        {
            ApplyVisualBudget(_visualBudget);
            RefreshTyping();
            if (_typing.Count > 0) EnsureTypingTimer();
        }
    }

    public void ApplyVisualBudget(VisualResourceBudget budget)
    {
        var previous = _visualBudget;
        _visualBudget = budget;
        Wallpaper.SetResourceBudget(budget.Suspended, budget.Animate);
        _previews.SetBudget(budget.ThumbnailBytes, budget.Downloads);
        if (budget.Suspended || previous.Animate != budget.Animate) ClearPlaybacks();
        if (budget.Suspended) ClearRailAvatars();
        if (!budget.Suspended && !IsUltraLightParked)
        {
            SyncMessages();
            if (SelectedInstance?.Context.Session is { } session) _ = LoadUserAvatarAsync(session.Me.Id);
        }
    }
}
