using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Chat.UI.Shortcuts;

namespace Chat.UI.Settings;

public partial class ShortcutSettingsView : UserControl
{
    public ShortcutSettingsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnCapturePointer, RoutingStrategies.Tunnel);
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShortcutSettingsViewModel settings || !settings.IsRecording) return;
        var backspace = e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None;
        if (settings.HandleCapture(KeyChord.FromKeyEvent(e), backspace))
            e.Handled = true;
    }

    private void OnCapturePointer(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ShortcutSettingsViewModel settings || !settings.IsRecording) return;
        if (InsideCaptureTarget(e.Source)) return;
        settings.CancelCapture();
    }

    private static bool InsideCaptureTarget(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control control && (control.Classes.Contains("shortcutHit") || control.Classes.Contains("shortcutReset")))
                return true;
        }
        return false;
    }
}
