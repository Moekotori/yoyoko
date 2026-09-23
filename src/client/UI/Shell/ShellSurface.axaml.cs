using Avalonia.Controls;
using Avalonia.Input;

namespace Chat.UI.Shell;

// Rebuildable visual tree; the persistent ShellViewModel owns sessions and drafts.
public partial class ShellSurface : UserControl
{
    public ShellSurface() => InitializeComponent();

    private void OnJumpBackdrop(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.CloseJump();
        e.Handled = true;
    }

    private void OnAddInstanceBackdrop(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.CloseAddInstance.Execute(null);
        e.Handled = true;
    }

    private void OnVoiceChannelSettingsBackdrop(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ShellViewModel { VoiceChannelSettings: { } settings }) settings.Close.Execute(null);
        e.Handled = true;
    }
}
