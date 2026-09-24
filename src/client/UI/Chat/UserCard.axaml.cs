using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Chat.UI.Shell;

namespace Chat.UI.Chat;

public partial class UserCard : UserControl
{
    public UserCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RequestBanner();
        AttachedToVisualTree += (_, _) => RequestBanner();
    }

    private void RequestBanner()
    {
        if (DataContext is MemberProfile member && Shell() is { } shell)
            shell.RequestBanner(member);
    }

    private async void CopyId(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MemberProfile member)
            await MemberGestures.CopyAsync(this, member.IdText);
        CloseFlyout();
        e.Handled = true;
    }

    private async void CopyUsername(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MemberProfile { HasHandle: true } member)
            await MemberGestures.CopyAsync(this, member.Username);
        CloseFlyout();
        e.Handled = true;
    }

    private void Message(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MemberProfile member && Shell() is { } shell)
            shell.MessageMember(member);
        CloseFlyout();
        e.Handled = true;
    }

    private void Mention(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MemberProfile member && Shell() is { } shell)
            shell.MentionMember(member);
        CloseFlyout();
        e.Handled = true;
    }

    private void EditProfile(object? sender, RoutedEventArgs e)
    {
        if (Shell() is { } shell)
            shell.OpenProfile.Execute(null);
        CloseFlyout();
        e.Handled = true;
    }

    private void ChangeBanner(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MemberProfile { IsSelf: true } && Shell() is { } shell)
            shell.Settings.Profile.ChangeBanner.Execute(null);
        e.Handled = true;
    }

    private ShellViewModel? Shell() => TopLevel.GetTopLevel(this)?.DataContext as ShellViewModel;

    private void CloseFlyout()
    {
        foreach (var visual in this.GetVisualAncestors())
            if (visual is Popup popup)
            {
                popup.Close();
                return;
            }
    }
}
