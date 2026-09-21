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
    private string _status = "";
    private string _connectedAddress = "";
    private string _serverName = "";
    private int? _protocol;
    private int? _api;
    private int? _latencyMs;
    private bool _latencyFailed;
    private bool _isBusy;
    private bool _isConnected;
    private CancellationTokenSource? _watch;
    public ConnectionSettingsViewModel(Func<Task> connect, Func<Task> disconnect,
        Func<CancellationToken, Task<int>> probe, Action<Exception> onError, I18n text)
    {
        _probe = probe;
        _text = text;
        Connect = new(connect, onError);
        Disconnect = new(disconnect, onError);
        text.PropertyChanged += OnTextChanged;
    }
    public AsyncCommand Connect { get; }
    public AsyncCommand Disconnect { get; }
    public string Address
    {
        get => _address;
        set
        {
            if (_address == value) return;
            _address = value;
            Changed();
            Changed(nameof(ShowConnect));
            Changed(nameof(CanConnect));
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
    public bool ShowDisconnect => IsConnected;
    public bool CanConnect => ShowConnect && !IsBusy;
    public bool CanDisconnect => ShowDisconnect && !IsBusy;
    public string ServerName => _serverName;
    public bool HasName => IsConnected && _serverName.Length > 0;
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
        var identityChanged = connected != _isConnected || address != _connectedAddress;
        _connectedAddress = address;
        _serverName = name;
        _protocol = protocol;
        _api = api;
        if (connected && address.Length > 0) Address = address;
        if (identityChanged)
        {
            _latencyMs = null;
            _latencyFailed = false;
        }
        IsConnected = connected;
        Changed(nameof(ServerName));
        Changed(nameof(HasName));
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
        try
        {
            var ms = await _probe(token);
            if (token.IsCancellationRequested) return;
            _latencyMs = ms;
            _latencyFailed = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch
        {
            if (token.IsCancellationRequested) return;
            _latencyMs = null;
            _latencyFailed = true;
        }
        Changed(nameof(LatencyText));
    }

    private void NotifyActions()
    {
        Changed(nameof(ShowConnect));
        Changed(nameof(ShowDisconnect));
        Changed(nameof(CanConnect));
        Changed(nameof(CanDisconnect));
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
