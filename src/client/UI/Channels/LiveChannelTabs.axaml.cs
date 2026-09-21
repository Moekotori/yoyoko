using Avalonia.Controls;
using Avalonia.Input;
using Chat.UI.Shell;

namespace Chat.UI.Channels;

public partial class LiveChannelTabs : UserControl
{
    public LiveChannelTabs() => InitializeComponent();

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint((Control)sender!).Properties.IsMiddleButtonPressed) return;
        if (sender is not Control { DataContext: ChannelItem channel } || DataContext is not ShellViewModel shell) return;
        shell.CloseOpenChannel.Execute(channel);
        e.Handled = true;
    }
}
