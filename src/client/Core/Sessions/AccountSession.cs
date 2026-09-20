using Chat.Core.Api;
using Chat.Core.Realtime;
using Chat.Core.Voice;
using Chat.Domain.Community;
using Chat.Domain.Instances;
using Chat.Protocol;

namespace Chat.Core.Sessions;

public sealed class AccountSession : IAsyncDisposable
{
    private readonly IChatApi _api;
    private readonly IVoiceMedia _media;
    private readonly CancellationTokenSource _lifetime = new();
    private GatewaySession? _gateway;
    public AccountSession(InstanceContext instance, InstanceDiscovery discovery, AuthResponse auth, IChatApi api, IVoiceMedia media)
    {
        Instance = instance;
        Discovery = discovery;
        Auth = auth;
        _api = api;
        _media = media;
        Account = new Account(new(instance.Descriptor.Id, auth.User.Id), auth.User.DisplayName);
        Voice = new VoiceRuntime(api, media, discovery.Api, auth.AccessToken);
    }
    public InstanceContext Instance { get; }
    public InstanceDiscovery Discovery { get; }
    public AuthResponse Auth { get; }
    public Account Account { get; }
    public VoiceRuntime Voice { get; }
    public IReadOnlyList<ServerDto> Servers { get; private set; } = [];
    public IReadOnlyList<ChannelDto> Channels { get; private set; } = [];
    public event Action? Changed;

    public static async Task<AccountSession> OpenAsync(InstanceContext instance, InstanceDiscovery discovery,
        AuthResponse auth, IChatApi api, Func<IGatewayConnection> gateways, IVoiceMedia media, CancellationToken cancellationToken)
    {
        var session = new AccountSession(instance, discovery, auth, api, media);
        var gateway = new GatewaySession(gateways());
        gateway.Event += session.OnGateway;
        await gateway.StartAsync(discovery.Gateway, auth.AccessToken, cancellationToken);
        session._gateway = gateway;
        instance.AttachAccount(session.Account);
        return session;
    }

    public async Task<ServerDto> CreateServerAsync(string name, CancellationToken cancellationToken)
    {
        var server = await _api.CreateServerAsync(Discovery.Api, Auth.AccessToken, name, cancellationToken);
        var channels = await _api.ListChannelsAsync(Discovery.Api, Auth.AccessToken, server.Id, cancellationToken);
        Servers = Servers.Append(server).ToArray();
        Channels = Channels.Concat(channels).ToArray();
        Changed?.Invoke();
        return server;
    }

    public async Task JoinInviteAsync(string code, CancellationToken cancellationToken)
    {
        var server = await _api.JoinServerAsync(Discovery.Api, Auth.AccessToken, code, cancellationToken);
        var channels = await _api.ListChannelsAsync(Discovery.Api, Auth.AccessToken, server.Id, cancellationToken);
        Servers = Servers.Append(server).ToArray();
        Channels = Channels.Concat(channels).ToArray();
        Changed?.Invoke();
    }

    private void OnGateway(GatewayEnvelope envelope)
    {
        if (envelope.Op != "dispatch" || envelope.Event is null) return;
        if (envelope.Event == "READY")
        {
            var ready = envelope.Data.Deserialize(ProtocolJson.Default.GatewayReady);
            if (ready is null) return;
            Servers = ready.Servers;
            Channels = ready.Channels;
            Voice.Replace(ready.VoiceStates ?? []);
            Changed?.Invoke();
            return;
        }
        if (envelope.Event == "VOICE_STATE_UPDATE")
        {
            var state = envelope.Data.Deserialize(ProtocolJson.Default.VoiceStateDto);
            if (state is not null) Voice.Apply(state);
            Changed?.Invoke();
        }
        if (envelope.Event == "CHANNEL_CREATE")
        {
            var channel = envelope.Data.Deserialize(ProtocolJson.Default.ChannelDto);
            if (channel is not null) Channels = Channels.Append(channel).ToArray();
            Changed?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await Voice.DisposeAsync();
        if (_gateway is not null) await _gateway.DisposeAsync();
        try { await _api.LogoutAsync(Discovery.Api, Auth.AccessToken, CancellationToken.None); }
        catch (Exception) { }
        _lifetime.Dispose();
    }
}
