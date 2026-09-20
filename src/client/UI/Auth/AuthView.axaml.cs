using Avalonia.Controls;
using Avalonia.Input;
using Chat.UI.Shell;

namespace Chat.UI.Auth;

public partial class AuthView : UserControl
{
    public AuthView() => InitializeComponent();

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ShellViewModel shell) return;
        if (shell.AuthForm.Submit.CanExecute(null)) shell.AuthForm.Submit.Execute(null);
        e.Handled = true;
    }
}
