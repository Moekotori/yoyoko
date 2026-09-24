using Avalonia.Threading;
using Chat.Core.Instances;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Settings;

public sealed class ConnectionSettingsViewModel : ObservableObject, IDisposable
{
    private readonly I18n _text;
    private readonly Func<CancellationToken, Task<int>> _probe;
    private string _address = "";
    private string _username = "";
    private string _status = "";
    private string _connectedAddress = "";
    private string _serverName = "";
    private string _host = "";
    private string _communityName = "";
    private string _inviteCode = "";
    private int? _protocol;
    private int? _api;
    private int? _latencyMs;
    private bool _latencyFailed;
    private bool _isBusy;
    private bool _isConnected;
    private bool _detailsExpanded;
    private CancellationTokenSource? _watch;
    public ConnectionSettingsViewModel(Func<Task> connect, Func<Task> disconnect,
        Func<CancellationToken, Task<int>> probe, Action cancel, Action<Exception> onError, I18n text)
    {
        _probe = probe;
        _text = text;
        Connect = new(connect, onError);
        Disconnect = new(disconnect, onError);
        Cancel = new(_ => cancel());
        ToggleDetails = new(_ => DetailsExpanded = !DetailsExpanded);
        text.PropertyChanged += OnTextChanged;
    }
    public AsyncCommand Connect { get; }
    public AsyncCommand Disconnect { get; }
    public ActionCommand Cancel { get; }
    public ActionCommand ToggleDetails { get; }
    public bool DetailsExpanded
    {
        get => _detailsExpanded;
        private set { if (_detailsExpanded == value) return; _detailsExpanded = value; Changed(); }
    }
    public string Address
    {
        get => _address;
        set
        {
            if (_address == value) return;
            _address = value;
            Changed();
            Changed(nameof(ShowConnect));
            Changed(nameof(ShowUsername));
            Changed(nameof(CanConnect));
        }
    }
    public string Username
    {
        get => _username;
        set
        {
            if (_username == value) return;
            _username = value;
            Changed();
        }
    }
    public string Status
    {
        get => _status;
        internal set { _status = value; Changed(); Changed(nameof(HasStatus)); }
    }
    public bool HasStatus => Status.Length > 0;
    public bool IsBusy
    {
        get => _isBusy;
        internal set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            Changed();
            NotifyActions();
        }
    }
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected == value) return;
            _isConnected = value;
            Changed();
            Changed(nameof(StateText));
            NotifyActions();
        }
    }
    public bool ShowConnect => !IsConnected || !SameAddress(Address, _connectedAddress);
    public bool ShowUsername => ShowConnect;
    public bool ShowDisconnect => IsConnected;
    public bool CanConnect => ShowConnect && !IsBusy;
    public bool CanDisconnect => ShowDisconnect && !IsBusy;
    public bool ShowCancel => IsBusy;
    public bool CanCancel => IsBusy;
    public string ServerName => _serverName;
    public bool HasName => IsConnected && _serverName.Length > 0;
    public string Host => _host;
    public bool HasHost => IsConnected && _host.Length > 0;
    public string CommunityName => _communityName;
    public bool HasCommunity => IsConnected && _communityName.Length > 0;
    public string InviteCode => _inviteCode;
    public string InviteLink => _inviteCode.Length == 0 || _connectedAddress.Length == 0
        ? ""
        : WorkspaceInvite.Link(new Uri(_connectedAddress), _inviteCode);
    public bool HasInvite => IsConnected && InviteLink.Length > 0;
    public bool HasProtocol => IsConnected && _protocol is not null && _api is not null;
    public string StateText => _text.Get(IsConnected ? TextKey.ConnectedServer : TextKey.NotConnected);
    public string LatencyText => _latencyMs is int ms
        ? _text.Get(TextKey.LatencyMs, ms)
        : _latencyFailed ? _text.Get(TextKey.LatencyUnreachable) : _text.Get(TextKey.LatencyMeasuring);
    public string ProtocolText => HasProtocol ? _text.Get(TextKey.ProtocolDetail, _protocol!, _api!) : "";

    public void Bind(InstanceContext? context)
    {
        var connected = context?.Session is not null;
        var address = context?.Descriptor.BaseUrl.AbsoluteUri ?? "";
        var name = context?.Discovery?.Name ?? context?.Descriptor.DisplayName ?? "";
        var protocol = context?.Discovery?.ProtocolVersion;
        var api = context?.Discovery?.ApiVersion;
        var community = context?.Session?.Servers.FirstOrDefault();
        var identityChanged = connected != _isConnected || address != _connectedAddress;
        _connectedAddress = address;
        _serverName = name;
        _host = context?.Descriptor.BaseUrl.Authority ?? "";
        _communityName = community?.Name ?? "";
        _inviteCode = community?.InviteCode ?? "";
        _protocol = protocol;
        _api = api;
        if (connected && address.Length > 0) Address = address;
        if (identityChanged)
        {
            DetailsExpanded = false;
            _latencyMs = null;
            _latencyFailed = false;
        }
        IsConnected = connected;
        Changed(nameof(ServerName));
        Changed(nameof(HasName));
        Changed(nameof(Host));
        Changed(nameof(HasHost));
        Changed(nameof(CommunityName));
        Changed(nameof(HasCommunity));
        Changed(nameof(InviteCode));
        Changed(nameof(InviteLink));
        Changed(nameof(HasInvite));
        Changed(nameof(HasProtocol));
        Changed(nameof(ProtocolText));
        Changed(nameof(LatencyText));
        NotifyActions();
        if (!connected) SetWatching(false);
        else if (_watch is not null) _ = ProbeOnceAsync(_watch.Token);
    }

    public void SetWatching(bool watch)
    {
        if (watch)
        {
            if (_watch is not null || !IsConnected) return;
            _watch = new();
            _ = WatchAsync(_watch.Token);
            return;
        }
        if (_watch is null) return;
        _watch.Cancel();
        _watch.Dispose();
        _watch = null;
    }

    public void Dispose()
    {
        _text.PropertyChanged -= OnTextChanged;
        SetWatching(false);
    }

    private async Task WatchAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await ProbeOnceAsync(token);
            try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ProbeOnceAsync(CancellationToken token)
    {
        if (!IsConnected) return;
        int? ms = null;
        var failed = false;
        try { ms = await _probe(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch { failed = true; }
        if (token.IsCancellationRequested) return;
        void Apply()
        {
            if (token.IsCancellationRequested || !IsConnected) return;
            _latencyMs = ms;
            _latencyFailed = failed;
            Changed(nameof(LatencyText));
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    private void NotifyActions()
    {
        Changed(nameof(ShowConnect));
        Changed(nameof(ShowUsername));
        Changed(nameof(ShowDisconnect));
        Changed(nameof(CanConnect));
        Changed(nameof(CanDisconnect));
        Changed(nameof(ShowCancel));
        Changed(nameof(CanCancel));
    }

    private void OnTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        Changed(nameof(StateText));
        Changed(nameof(LatencyText));
        Changed(nameof(ProtocolText));
    }

    private static bool SameAddress(string left, string right) =>
        left.Trim().TrimEnd('/').Equals(right.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
