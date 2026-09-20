using System.Collections.Concurrent;
using System.Text.Json;
using Chat.Core;
using Chat.Core.Instances;
using Chat.Core.Messaging;
using Chat.Core.Realtime;
using Chat.Core.Voice;
using Chat.Domain.Instances;
using Chat.Localization;
using Chat.Protocol;

namespace Chat.Core.Sessions;

public sealed class InstanceSession : IAsyncDisposable
{
    private readonly IChatApi _api;
    private readonly IMessageCache _cache;
    private readonly ICredentialVault _vault;
    private readonly Func<IGatewayConnection> _gateways;
    private IGatewayConnection? _gateway;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<Guid, UserDto> _users = [];
    private readonly string _accessToken;
    private readonly string _refreshToken;
    private Task? _loop;
    public InstanceSession(InstanceDescriptor descriptor, UserDto me, IChatApi api, IMessageCache cache,
        ICredentialVault vault, Func<IGatewayConnection> gateways, IVoiceMedia media, string accessToken, string refreshToken)
    {
        Descriptor = descriptor;
        Me = me;
        Account = ToAccount(descriptor, me);
        _api = api;
        _cache = cache;
        _vault = vault;
        _gateways = gateways;
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _api.SetAccessToken(accessToken);
        Scope = new(descriptor.Id, me.Id);
        Voice = new VoiceRuntime(api, media, me.Id);
        _users[me.Id] = me;
    }
    public InstanceDescriptor Descriptor { get; }
    public Account Account { get; private set; }
    public UserDto Me { get; private set; }
    public CacheScope Scope { get; }
    public IReadOnlyList<ServerDto> Servers { get; private set; } = [];
    public IReadOnlyList<ChannelDto> Channels { get; private set; } = [];
    public VoiceRuntime Voice { get; }
    public long MaxAttachmentBytes { get; private set; } = ProtocolVersion.MaxAttachmentBytes;
    public int MaxAttachments { get; private set; } = ProtocolVersion.MaxAttachmentsPerMessage;
    public event Action? CommunityChanged;
    public event Action<MessageDto>? MessageArrived;

    public void ApplyDiscovery(InstanceDiscovery info)
    {
        MaxAttachmentBytes = FileKinds.EffectiveLimit(info.MaxAttachmentBytes);
        MaxAttachments = FileKinds.EffectiveCount(info.MaxAttachmentsPerMessage);
    }

    public static async Task<InstanceSession> SignInAsync(InstanceDescriptor descriptor, IChatApi api,
        IMessageCache cache, ICredentialVault vault, Func<IGatewayConnection> gateways, IVoiceMedia media,
        string username, string password, string? displayName, bool register, CancellationToken cancellationToken)
    {
        var auth = register
            ? await api.RegisterAsync(new(username, displayName ?? username, password), cancellationToken)
            : await api.LoginAsync(new(username, password), cancellationToken);
        var session = new InstanceSession(descriptor, auth.User, api, cache, vault, gateways, media, auth.AccessToken, auth.RefreshToken);
        await session.PersistAuthAsync(cancellationToken);
        session.Start();
        return session;
    }

    public static async Task<InstanceSession?> RestoreAsync(InstanceDescriptor descriptor, IChatApi api,
        IMessageCache cache, ICredentialVault vault, Func<IGatewayConnection> gateways, IVoiceMedia media, CancellationToken cancellationToken)
    {
        var accountId = await cache.GetSettingAsync("account:" + descriptor.Id.Value, cancellationToken);
        if (accountId is null || !Guid.TryParse(accountId, out var id)) return null;
        var refresh = await vault.GetAsync(descriptor.Id, id, cancellationToken);
        if (refresh is null) return null;
        try
        {
            var auth = await api.RefreshAsync(refresh, cancellationToken);
            var session = new InstanceSession(descriptor, auth.User, api, cache, vault, gateways, media, auth.AccessToken, auth.RefreshToken);
            await session.PersistAuthAsync(cancellationToken);
            var cached = await cache.LoadCommunityAsync(session.Scope, cancellationToken);
            session.ApplyCommunity(cached);
            session.Start();
            return session;
        }
        catch { return null; }
    }

    public string AuthorName(Guid userId) =>
        _users.TryGetValue(userId, out var user) ? user.DisplayName : userId.ToString()[..8];

    public UserDto? User(Guid userId) => _users.TryGetValue(userId, out var user) ? user : null;

    public async Task<UserDto> PatchProfileAsync(string? username, string? displayName, Guid? avatarId, bool clearAvatar,
        CancellationToken cancellationToken)
    {
        var user = await _api.PatchMeAsync(new(username, displayName, avatarId, clearAvatar), cancellationToken);
        ApplyUser(user);
        await PersistAuthAsync(cancellationToken);
        await _cache.SaveCommunityAsync(Scope, new(Servers, Channels, [.. _users.Values]), cancellationToken);
        CommunityChanged?.Invoke();
        return user;
    }

    public async Task<UserDto> ChangeAvatarAsync(PickedFile file, CancellationToken cancellationToken)
    {
        if (file.Size <= 0 || file.Size > ProtocolVersion.MaxAvatarBytes)
            throw new ClientFault(TextKey.FileTooLarge, file.FileName, FileKinds.SizeLabel(ProtocolVersion.MaxAvatarBytes));
        if (!FileKinds.IsImage(file.MimeType))
            throw new ClientFault(TextKey.InvalidAvatar);
        var uploaded = await _api.UploadAsync(file, cancellationToken);
        return await PatchProfileAsync(null, null, uploaded.Id, false, cancellationToken);
    }

    public async Task<ServerDto> CreateServerAsync(string name, CancellationToken cancellationToken)
    {
        var server = await _api.CreateServerAsync(name, cancellationToken);
        await RefreshCommunityAsync(cancellationToken);
        return server;
    }

    public async Task JoinAsync(string invite, CancellationToken cancellationToken)
    {
        await _api.JoinAsync(invite, cancellationToken);
        await RefreshCommunityAsync(cancellationToken);
    }

    public async Task<ServerDto> PatchModerationAsync(Guid serverId, string[] words, int cooldownSeconds,
        CancellationToken cancellationToken)
    {
        var server = await _api.PatchModerationAsync(serverId, new(words, cooldownSeconds), cancellationToken);
        Servers = Servers.Select(item => item.Id == server.Id ? server : item).ToArray();
        await _cache.SaveCommunityAsync(Scope, new(Servers, Channels, [.. _users.Values]), cancellationToken);
        CommunityChanged?.Invoke();
        return server;
    }

    public ChannelTimeline OpenChannel(Guid channelId) =>
        new(Scope, channelId, Account.Key.Id, _api, _cache);

    public Task<byte[]?> DownloadAsync(Uri url, int maxBytes, CancellationToken cancellationToken) =>
        _api.DownloadAsync(url, maxBytes, cancellationToken);

    public async Task DownloadToAsync(AttachmentDto attachment, Stream destination, CancellationToken cancellationToken)
    {
        var limit = Math.Max(attachment.Size, 1) + 65_536;
        try
        {
            await _api.DownloadToAsync(attachment.DownloadUrl, destination, limit, cancellationToken);
        }
        catch
        {
            if (destination.CanSeek)
            {
                destination.Position = 0;
                destination.SetLength(0);
            }
            var authed = new Uri(_api.ApiBase.AbsoluteUri.TrimEnd('/') + $"/attachments/{attachment.Id:D}/content");
            await _api.DownloadToAsync(authed, destination, limit, cancellationToken);
        }
    }

    public async Task SignOutAsync()
    {
        try { await _api.LogoutAsync(_lifetime.Token); } catch { /* already invalid */ }
        await _vault.RemoveAsync(Descriptor.Id, Account.Key.Id, CancellationToken.None);
        await _cache.PurgeAsync(Scope, CancellationToken.None);
        await DisposeAsync();
    }

    public void Start()
    {
        _loop ??= Task.Run(() => RunAsync(_lifetime.Token));
    }

    private async Task PersistAuthAsync(CancellationToken cancellationToken)
    {
        await _vault.StoreAsync(Descriptor.Id, Account.Key.Id, _refreshToken, cancellationToken);
        await _cache.SaveAccountAsync(Scope, Account.DisplayName, cancellationToken);
        await _cache.SetSettingAsync("account:" + Descriptor.Id.Value, Account.Key.Id.ToString(), cancellationToken);
        _users[Me.Id] = Me;
    }

    private async Task RefreshCommunityAsync(CancellationToken cancellationToken)
    {
        var servers = await _api.ListServersAsync(cancellationToken);
        var channels = new List<ChannelDto>();
        foreach (var server in servers)
            channels.AddRange(await _api.ListChannelsAsync(server.Id, cancellationToken));
        ApplyCommunity(new(servers, channels, [.. _users.Values]));
        await _cache.SaveCommunityAsync(Scope, new(Servers, Channels, [.. _users.Values]), cancellationToken);
        CommunityChanged?.Invoke();
    }

    private void ApplyCommunity(CommunitySnapshot snapshot)
    {
        Servers = snapshot.Servers;
        Channels = snapshot.Channels;
        foreach (var user in snapshot.Users) ApplyUser(user);
    }

    private void ApplyUser(UserDto user)
    {
        _users[user.Id] = user;
        if (user.Id != Me.Id) return;
        Me = user;
        Account = ToAccount(Descriptor, user);
    }

    private static Account ToAccount(InstanceDescriptor descriptor, UserDto user) =>
        new(new(descriptor.Id, user.Id), user.Username, user.DisplayName);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(cancellationToken);
                attempt = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch
            {
                attempt++;
                try { await Task.Delay(ReconnectPolicy.Delay(attempt, Random.Shared.NextDouble()), cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ConnectOnceAsync(CancellationToken cancellationToken)
    {
        if (_gateway is not null) await _gateway.DisposeAsync();
        _gateway = _gateways();
        await _gateway.ConnectAsync(_api.Gateway, cancellationToken);
        await using var events = _gateway.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        if (!await events.MoveNextAsync() || events.Current.Op != "hello") throw new InvalidDataException("Expected hello.");
        var hello = events.Current.Data.Deserialize(ProtocolJson.Default.GatewayHello)
            ?? new GatewayHello(1, 30_000);
        var cursor = await _cache.LoadCursorAsync(Scope, cancellationToken);
        if (cursor.SessionId is { Length: > 0 } session)
        {
            var resume = JsonSerializerElement(new GatewayResume(1, _accessToken, session, cursor.Seq), ProtocolJson.Default.GatewayResume);
            await _gateway.SendAsync(new("resume", null, null, resume), cancellationToken);
        }
        else
        {
            var identify = JsonSerializerElement(new GatewayIdentify(1, _accessToken), ProtocolJson.Default.GatewayIdentify);
            await _gateway.SendAsync(new("identify", null, null, identify), cancellationToken);
        }
        using var heartbeat = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, heartbeat.Token);
        var beat = BeatAsync(Math.Max(hello.HeartbeatIntervalMs, 5000), linked.Token);
        try
        {
            while (await events.MoveNextAsync())
            {
                var envelope = events.Current;
                if (envelope.Op == "invalid_session")
                {
                    await _cache.SaveCursorAsync(Scope, null, 0, cancellationToken);
                    break;
                }
                if (envelope.Op == "heartbeat_ack") continue;
                if (envelope.Op != "dispatch") continue;
                await ApplyAsync(envelope, cancellationToken);
            }
        }
        finally
        {
            heartbeat.Cancel();
            try { await beat; } catch { /* cancelled */ }
        }
    }

    private async Task BeatAsync(int intervalMs, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 5_000, 60_000));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            if (_gateway is null) return;
            await _gateway.SendAsync(new("heartbeat", null, null, JsonDocument.Parse("{}").RootElement.Clone()), cancellationToken);
        }
    }

    private async Task ApplyAsync(GatewayEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Event == "READY")
        {
            var ready = envelope.Data.Deserialize(ProtocolJson.Default.ReadyDto) ?? throw new InvalidDataException("READY");
            ApplyCommunity(new(ready.Servers, ready.Channels, ready.Users));
            ApplyUser(ready.User);
            Voice.Replace(ready.VoiceStates);
            await _cache.SaveCommunityAsync(Scope, new(Servers, Channels, [.. _users.Values]), cancellationToken);
            await _cache.SaveCursorAsync(Scope, ready.SessionId, envelope.Seq ?? 0, cancellationToken);
            CommunityChanged?.Invoke();
            return;
        }
        if (envelope.Event == "USER_UPDATE")
        {
            var user = envelope.Data.Deserialize(ProtocolJson.Default.UserDto);
            if (user is not null)
            {
                ApplyUser(user);
                await _cache.SaveCommunityAsync(Scope, new(Servers, Channels, [.. _users.Values]), cancellationToken);
                CommunityChanged?.Invoke();
            }
            if (envelope.Seq is long userSeq)
            {
                var cursor = await _cache.LoadCursorAsync(Scope, cancellationToken);
                await _cache.SaveCursorAsync(Scope, cursor.SessionId, userSeq, cancellationToken);
            }
            return;
        }
        if (envelope.Event == "VOICE_STATE_UPDATE")
        {
            var state = envelope.Data.Deserialize(ProtocolJson.Default.VoiceStateDto);
            if (state is not null) Voice.Apply(state);
            CommunityChanged?.Invoke();
            if (envelope.Seq is long voiceSeq)
            {
                var cursor = await _cache.LoadCursorAsync(Scope, cancellationToken);
                await _cache.SaveCursorAsync(Scope, cursor.SessionId, voiceSeq, cancellationToken);
            }
            return;
        }
        if (envelope.Event == "MESSAGE_CREATE")
        {
            var message = envelope.Data.Deserialize(ProtocolJson.Default.MessageDto);
            if (message is null) return;
            await _cache.UpsertAsync(Scope, message, cancellationToken);
            if (envelope.Seq is long seq)
            {
                var cursor = await _cache.LoadCursorAsync(Scope, cancellationToken);
                await _cache.SaveCursorAsync(Scope, cursor.SessionId, seq, cancellationToken);
            }
            MessageArrived?.Invoke(message);
            return;
        }
        if (envelope.Event is "CHANNEL_CREATE" or "CHANNEL_UPDATE" or "SERVER_CREATE" or "MEMBER_JOIN")
        {
            try { await RefreshCommunityAsync(cancellationToken); } catch { /* next READY/resync */ }
            if (envelope.Seq is long seq)
            {
                var cursor = await _cache.LoadCursorAsync(Scope, cancellationToken);
                await _cache.SaveCursorAsync(Scope, cursor.SessionId, seq, cancellationToken);
            }
        }
        else if (envelope.Seq is long ignored)
        {
            var cursor = await _cache.LoadCursorAsync(Scope, cancellationToken);
            await _cache.SaveCursorAsync(Scope, cursor.SessionId, ignored, cancellationToken);
        }
    }

    private static JsonElement JsonSerializerElement<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
        JsonSerializer.SerializeToElement(value, info);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_loop is not null)
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* shutdown */ }
        await Voice.DisposeAsync();
        if (_gateway is not null) await _gateway.DisposeAsync();
        _api.Dispose();
        _lifetime.Dispose();
    }
}
