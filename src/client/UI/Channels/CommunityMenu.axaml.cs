using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Chat.UI.Shell;

namespace Chat.UI.Channels;

public partial class CommunityMenu : UserControl
{
    public CommunityMenu() => InitializeComponent();

    private void OnSubmitKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ShellViewModel shell) return;
        var command = ((sender as Control)?.Tag as string) switch
        {
            "create" => (ICommand)shell.CreateServer,
            "join" => shell.JoinServer,
            "cooldown" => shell.SaveModeration,
            _ => null
        };
        if (command?.CanExecute(null) != true) return;
        command.Execute(null);
        e.Handled = true;
    }
}
