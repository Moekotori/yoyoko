using Avalonia.Controls;
using Chat.UI.Components;
using Avalonia.Input;
using Chat.UI.Shell;

namespace Chat.UI.Chat;

public partial class LiveAccountBar : UserControl
{
    private DateTime _lastRefresh;
    public LiveAccountBar()
    {
        InitializeComponent();
        InputDeviceButton.Command = new ActionCommand(_ =>
        {
            OutputDevicePopup.IsOpen = false;
            InputDevicePopup.IsOpen = !InputDevicePopup.IsOpen;
        });
        OutputDeviceButton.Command = new ActionCommand(_ =>
        {
            InputDevicePopup.IsOpen = false;
            OutputDevicePopup.IsOpen = !OutputDevicePopup.IsOpen;
        });
    }
    private void OnDeviceChosen(object? sender, EventArgs e)
    {
        InputDevicePopup.IsOpen = false;
        OutputDevicePopup.IsOpen = false;
    }
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
