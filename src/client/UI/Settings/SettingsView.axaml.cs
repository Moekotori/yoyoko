using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;

namespace Chat.UI.Settings;

public partial class SettingsView : UserControl
{
    private SettingsMotionScope? _motion;
    public SettingsView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _motion = new(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _motion?.Dispose();
        _motion = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SettingsViewModel settings)
        {
            settings.Close.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
