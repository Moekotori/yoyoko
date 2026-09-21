using Avalonia.Controls;
using Avalonia.Input;
using Chat.UI.Shell;

namespace Chat.UI.Chat;

public partial class LiveAccountBar : UserControl
{
    private DateTime _lastRefresh;
    public LiveAccountBar() => InitializeComponent();
    private async void OnDevicesHover(object? sender, PointerEventArgs e)
    {
        if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(5)) return;
        _lastRefresh = DateTime.UtcNow;
        if (DataContext is ShellViewModel shell) await shell.Devices.RefreshAsync();
    }
    private async void OnDevicesOpened(object? sender, EventArgs e)
    {
        if (DataContext is ShellViewModel shell) await shell.Devices.RefreshAsync();
    }
}
