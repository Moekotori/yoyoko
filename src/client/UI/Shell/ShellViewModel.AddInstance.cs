using Chat.Core.Instances;
using Chat.Core.Sessions;
using Chat.Localization;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private bool _showAddInstance;
    private bool _isAddingInstance;
    private string _addAddress = "";
    private string _addServerPassword = "";
    private string _addInstanceStatus = "";
    private CancellationTokenSource? _addInstanceWork;
    private bool _dismissAddInstanceAfterCancel;

    public bool ShowAddInstance
    {
        get => _showAddInstance;
        private set { if (_showAddInstance == value) return; _showAddInstance = value; Changed(); }
    }
    public bool IsAddingInstance
    {
        get => _isAddingInstance;
        private set { if (_isAddingInstance == value) return; _isAddingInstance = value; Changed(); }
    }
    public string AddAddress
    {
        get => _addAddress;
        set { _addAddress = value; Changed(); }
    }
    public string AddServerPassword
    {
        get => _addServerPassword;
        set { _addServerPassword = value; Changed(); }
    }
    public string AddInstanceStatus
    {
        get => _addInstanceStatus;
        private set { _addInstanceStatus = value; Changed(); }
    }

    private void OpenAddInstanceDialog()
    {
        if (ShowAddInstance) return;
        CloseJump();
        ProfileOpen = false;
        AddAddress = "";
        AddServerPassword = "";
        AddInstanceStatus = "";
        _dismissAddInstanceAfterCancel = false;
        ShowAddInstance = true;
    }

    private void CloseAddInstanceDialog()
    {
        if (IsAddingInstance)
        {
            _dismissAddInstanceAfterCancel = true;
            _addInstanceWork?.Cancel();
            return;
        }
        AddAddress = "";
        AddServerPassword = "";
        AddInstanceStatus = "";
        _dismissAddInstanceAfterCancel = false;
        ShowAddInstance = false;
    }

    private async Task AddAsync()
    {
        if (!ShowAddInstance || IsAddingInstance) return;
        Uri address;
        try { address = new Uri(WorkspaceAddress.Resolve(AddAddress, WorkspaceAddress.LocalServerUrl)); }
        catch (Exception error) { AddInstanceStatus = _text.Error(error); return; }

        using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _addInstanceWork = work;
        IsAddingInstance = true;
        AddInstanceStatus = _text.Get(TextKey.ConnectingServer);
        var connected = false;
        try
        {
            var context = await _connection.ConnectAsync(address.AbsoluteUri, work.Token,
                serverPassword: string.IsNullOrEmpty(AddServerPassword) ? null : AddServerPassword,
                updateSavedAddress: false);
            var item = TrackInstance(context);
            if (context.Session is { } session) BindSession(session);
            SelectedInstance = item;
            Connection.Address = context.Descriptor.BaseUrl.AbsoluteUri;
            Connection.Bind(context);
            Connection.Status = "";
            ShowSettings = false;
            Settings.Profile.Reload();
            connected = true;
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            AddInstanceStatus = _text.Get(TextKey.Cancelled);
        }
        catch (ChatApiException error) when (error.Code == "invalid_server_password")
        {
            AddInstanceStatus = _text.Get(TextKey.InvalidServerPassword);
        }
        catch (Exception error)
        {
            AddInstanceStatus = _text.Error(error);
        }
        finally
        {
            _addInstanceWork = null;
            IsAddingInstance = false;
            if (connected || _dismissAddInstanceAfterCancel) CloseAddInstanceDialog();
        }
    }
}
