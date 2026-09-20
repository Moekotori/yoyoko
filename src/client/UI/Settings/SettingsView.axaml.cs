using Avalonia.Controls;
using Avalonia.Input;

namespace Chat.UI.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

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
