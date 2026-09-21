using System.ComponentModel;
using Avalonia.Controls;
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

    private void OnChannelMenuOpening(object? sender, CancelEventArgs args)
    {
        if (sender is not ContextMenu menu || DataContext is not ShellViewModel shell) { args.Cancel = true; return; }
        var channel = (menu.Tag as Button)?.DataContext as ChannelItem;
        if (channel is null) { args.Cancel = true; return; }
        var open = (MenuItem)menu.Items[0]!;
        open.Command = shell.SelectOpenChannel;
        open.CommandParameter = channel;
        var canManage = shell.CanManageChannel(channel);
        for (var index = 1; index < menu.Items.Count; index++) ((Control)menu.Items[index]!).IsVisible = canManage;
        ((MenuItem)menu.Items[2]!).Command = new ActionCommand(_ => shell.EditChannel(channel, channel.IsVoice, ChannelEditMode.Rename));
        var create = (MenuItem)menu.Items[3]!;
        create.Header = I18n.Presenter.Get(channel.IsVoice ? TextKey.CreateVoiceChannel : TextKey.CreateTextChannel);
        create.Command = new ActionCommand(_ => shell.EditChannel(channel, channel.IsVoice, ChannelEditMode.Create));
        ((MenuItem)menu.Items[5]!).Command = new ActionCommand(_ => shell.EditChannel(channel, channel.IsVoice, ChannelEditMode.Delete));
    }
}
