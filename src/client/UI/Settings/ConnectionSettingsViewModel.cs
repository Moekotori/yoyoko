using Chat.UI.Components;

namespace Chat.UI.Settings;

public sealed class ConnectionSettingsViewModel : ObservableObject
{
    private string _address = "";
    private string _status = "";
    private bool _isBusy;
    public ConnectionSettingsViewModel(Func<Task> connect, Action<Exception> onError)
    {
        Connect = new(connect, onError);
    }
    public AsyncCommand Connect { get; }
    public string Address { get => _address; set { _address = value; Changed(); } }
    public string Status { get => _status; internal set { _status = value; Changed(); } }
    public bool IsBusy { get => _isBusy; internal set { _isBusy = value; Changed(); } }
}
