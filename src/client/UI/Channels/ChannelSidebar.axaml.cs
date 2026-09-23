using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Chat.Core.Instances;
using Chat.Motion;
using Chat.UI.Chat;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Shell;

namespace Chat.UI.Channels;

public partial class ChannelSidebar : UserControl
{
    public ChannelSidebar() => InitializeComponent();

    private void OnChannelLoaded(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button button) return;
        if (button.ContextMenu is { } menu) menu.Tag = button;
        if (button.DataContext is ChannelItem item && item.ConsumeEnter())
            ItemEnter.Play(button);
    }

    private void OnCardOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout flyout) MemberGestures.BindCard(flyout);
    }

    private void OnChannelMenuOpening(object? sender, CancelEventArgs args)
    {
        if (sender is not ContextMenu menu || DataContext is not ShellViewModel shell) { args.Cancel = true; return; }
        var channel = (menu.Tag as Button)?.DataContext as ChannelItem;
        if (channel is null) { args.Cancel = true; return; }
        var open = (MenuItem)menu.Items[0]!;
        open.Command = shell.SelectOpenChannel;
        open.CommandParameter = channel;
        ((MenuItem)menu.Items[1]!).Command = new ActionCommand(_ => shell.MarkChannelRead(channel));
        ((MenuItem)menu.Items[2]!).Command = new ActionCommand(_ => shell.MarkChannelUnread(channel));
        ((MenuItem)menu.Items[4]!).Command = new ActionCommand(_ => shell.SetChannelNotify(channel, global::Chat.Core.Messaging.ChannelNotify.All));
        ((MenuItem)menu.Items[5]!).Command = new ActionCommand(_ => shell.SetChannelNotify(channel, global::Chat.Core.Messaging.ChannelNotify.Mentions));
        ((MenuItem)menu.Items[6]!).Command = new ActionCommand(_ => shell.SetChannelNotify(channel, global::Chat.Core.Messaging.ChannelNotify.Mute));
        var canManage = shell.CanManageChannel(channel);
        for (var index = 7; index < menu.Items.Count; index++) ((Control)menu.Items[index]!).IsVisible = canManage;
        ((MenuItem)menu.Items[8]!).Command = new ActionCommand(_ => shell.EditChannel(channel, false, ChannelEditMode.Rename));
        var add = (MenuItem)menu.Items[9]!;
        add.Header = I18n.Presenter.Get(TextKey.CreateTextChannel);
        add.Command = new ActionCommand(_ => shell.EditChannel(channel, false, ChannelEditMode.Create));
        ((MenuItem)menu.Items[11]!).Command = new ActionCommand(_ => shell.EditChannel(channel, false, ChannelEditMode.Delete));
    }

    private void OnVoiceMenuOpening(object? sender, CancelEventArgs args)
    {
        if (sender is not ContextMenu menu || DataContext is not ShellViewModel shell) { args.Cancel = true; return; }
        var channel = (menu.Tag as Button)?.DataContext as ChannelItem;
        if (channel is null) { args.Cancel = true; return; }
        var join = (MenuItem)menu.Items[0]!;
        if (shell.IsJoinedVoice(channel))
        {
            join.Header = I18n.Presenter.Get(TextKey.LeaveVoice);
            join.Command = shell.LeaveVoice;
            join.CommandParameter = null;
        }
        else
        {
            join.Header = I18n.Presenter.Get(TextKey.JoinVoiceChannel);
            join.Command = shell.JoinVoice;
            join.CommandParameter = channel;
        }
        ((MenuItem)menu.Items[1]!).Command = new ActionCommand(_ => _ = CopyWebVoiceAsync(shell, channel));
        var settings = (MenuItem)menu.Items[2]!;
        settings.Command = new ActionCommand(_ => shell.OpenVoiceChannelSettings(channel));
        var canManage = shell.CanManageChannel(channel);
        for (var index = 3; index < menu.Items.Count; index++) ((Control)menu.Items[index]!).IsVisible = canManage;
        ((MenuItem)menu.Items[4]!).Command = new ActionCommand(_ => shell.EditChannel(channel, true, ChannelEditMode.Rename));
        var create = (MenuItem)menu.Items[5]!;
        create.Header = I18n.Presenter.Get(TextKey.CreateVoiceChannel);
        create.Command = new ActionCommand(_ => shell.EditChannel(channel, true, ChannelEditMode.Create));
        ((MenuItem)menu.Items[7]!).Command = new ActionCommand(_ => shell.EditChannel(channel, true, ChannelEditMode.Delete));
    }

    private async Task CopyWebVoiceAsync(ShellViewModel shell, ChannelItem channel)
    {
        var origin = shell.SelectedInstance?.Context.Descriptor.BaseUrl;
        if (origin is null || channel.IsFixture || !channel.IsVoice)
        {
            shell.Workspace.ShowNotice(I18n.T(TextKey.NeedInstance));
            return;
        }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            shell.Workspace.ShowNotice(I18n.T(TextKey.ClipboardUnavailable));
            return;
        }
        try
        {
            await ClipboardExtensions.SetTextAsync(clipboard, WorkspaceInvite.VoiceLink(origin, channel.Id));
            shell.Workspace.ShowNotice(I18n.T(TextKey.WebVoiceLinkCopied));
        }
        catch (Exception)
        {
            shell.Workspace.ShowNotice(I18n.T(TextKey.ClipboardFailed));
        }
    }
}
