using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Chat.Core.Instances;
using Chat.Localization;
using Chat.Motion;
using Chat.UI.Localization;
using Chat.UI.Shell;

namespace Chat.UI.Channels;

public partial class CommunityMenu : UserControl
{
    private static readonly string[] Sections = ["CreateCommunityPanel", "JoinCommunityPanel", "ModerationPanel"];

    public CommunityMenu() => InitializeComponent();

    private void ToggleSection(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string name }) return;
        foreach (var id in Sections)
        {
            var open = id == name && this.FindControl<RevealHost>(id) is { IsOpen: false };
            if (this.FindControl<RevealHost>(id) is { } panel) panel.IsOpen = open;
            if (this.FindControl<Control>(id + "Chevron") is { } chevron)
                chevron.Classes.Set("open", open);
        }
    }

    private void ChangeAvatar(object? sender, RoutedEventArgs e)
    {
        CloseHostFlyout();
        if (DataContext is ShellViewModel shell)
            shell.Settings.Profile.ChangeAvatar.Execute(null);
    }

    private void RemoveAvatar(object? sender, RoutedEventArgs e)
    {
        CloseHostFlyout();
        if (DataContext is ShellViewModel shell)
            shell.Settings.Profile.RemoveAvatar.Execute(null);
    }

    private async void CopyInvite(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell || !shell.HasInvite) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            shell.Workspace.ShowNotice(I18n.T(TextKey.ClipboardUnavailable));
            return;
        }
        try
        {
            var origin = shell.SelectedInstance?.Context.Descriptor.BaseUrl;
            var text = origin is null ? shell.InviteCode : WorkspaceInvite.Link(origin, shell.InviteCode);
            await ClipboardExtensions.SetTextAsync(clipboard, text);
            shell.Workspace.ShowNotice(I18n.T(TextKey.InviteCopied));
        }
        catch (Exception)
        {
            shell.Workspace.ShowNotice(I18n.T(TextKey.ClipboardFailed));
        }
    }

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

    private void SignOutClick(object? sender, RoutedEventArgs e)
    {
        CloseHostFlyout();
        if (DataContext is ShellViewModel shell)
            shell.SignOut.Execute(null);
    }

    private void CloseForChannelEdit(object? sender, RoutedEventArgs e) => CloseHostFlyout();

    private void CloseHostFlyout()
    {
        if (this.GetVisualAncestors().OfType<Popup>().FirstOrDefault() is { } popup)
            popup.IsOpen = false;
    }
}
