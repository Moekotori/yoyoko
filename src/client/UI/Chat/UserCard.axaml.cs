using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Chat.UI.Chat;

public partial class UserCard : UserControl
{
    public UserCard() => InitializeComponent();

    private async void CopyId(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MemberProfile member)
            await MemberGestures.CopyAsync(this, member.IdText);
        e.Handled = true;
    }
}
