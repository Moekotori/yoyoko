using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input;
using Chat.UI.Shortcuts;

namespace Chat.UI.Settings;

public partial class ShortcutSettingsView : UserControl
{
    public ShortcutSettingsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShortcutSettingsViewModel settings || !settings.IsRecording) return;
        var backspace = e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None;
        if (settings.HandleCapture(KeyChord.FromKeyEvent(e), backspace))
            e.Handled = true;
    }
}
