using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Chat.Localization;
using Chat.UI.Localization;

namespace Chat.UI.Settings;

public partial class ConnectionSettingsView : UserControl
{
    public ConnectionSettingsView() => InitializeComponent();

    private async void CopyInvite(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionSettingsViewModel connection || !connection.HasInvite) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            connection.Status = I18n.T(TextKey.ClipboardUnavailable);
            return;
        }
        try
        {
            await ClipboardExtensions.SetTextAsync(clipboard, connection.InviteLink);
            connection.Status = I18n.T(TextKey.InviteCopied);
        }
        catch (Exception)
        {
            connection.Status = I18n.T(TextKey.ClipboardFailed);
        }
    }
}
