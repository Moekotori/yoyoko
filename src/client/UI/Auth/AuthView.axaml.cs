using Avalonia.Controls;
using Avalonia.Input;


namespace Chat.UI.Auth;

public partial class AuthView : UserControl
{
    public AuthView() => InitializeComponent();

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AuthFormViewModel form) return;
        if (form.Submit.CanExecute(null)) form.Submit.Execute(null);
        e.Handled = true;
    }
}
