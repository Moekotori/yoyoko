using Avalonia.Controls;
using Avalonia.Input;
using Chat.UI.Shell;

namespace Chat.UI.Instances;

public partial class InstanceRail : UserControl
{
    public InstanceRail() => InitializeComponent();

    private void OnInstancePressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is ShellViewModel { ShowSettings: true } shell)
            shell.OpenSettings.Execute(null);
    }
}
