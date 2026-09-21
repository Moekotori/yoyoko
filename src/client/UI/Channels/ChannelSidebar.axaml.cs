using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        if (sender is Button { ContextMenu: { } menu } button) menu.Tag = button;
    }

    private void OnVoiceDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is not ShellViewModel shell || sender is not Control { DataContext: ChannelItem channel }) return;
        shell.JoinVoice.Execute(channel);
        args.Handled = true;
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
        var settings = (MenuItem)menu.Items[1]!;
        settings.Command = shell.SelectOpenChannel;
        settings.CommandParameter = channel;
        var canManage = shell.CanManageChannel(channel);
        for (var index = 2; index < menu.Items.Count; index++) ((Control)menu.Items[index]!).IsVisible = canManage;
        ((MenuItem)menu.Items[3]!).Command = new ActionCommand(_ => shell.EditChannel(channel, true, ChannelEditMode.Rename));
        var create = (MenuItem)menu.Items[4]!;
        create.Header = I18n.Presenter.Get(TextKey.CreateVoiceChannel);
        create.Command = new ActionCommand(_ => shell.EditChannel(channel, true, ChannelEditMode.Create));
        ((MenuItem)menu.Items[6]!).Command = new ActionCommand(_ => shell.EditChannel(channel, true, ChannelEditMode.Delete));
    }
}
