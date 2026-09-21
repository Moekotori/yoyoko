using System.Security.Cryptography;
using Chat.Core.Messaging;
using Chat.Core.Realtime;
using Chat.Core.Sessions;
using Chat.Core.Voice;
using Chat.Localization;

namespace Chat.Core.Instances;

// Passwordless first use through the existing authenticated registration protocol.
public sealed class WorkspaceConnection(InstanceManager instances, IMessageCache cache,
    ICredentialVault vault, IChatApiFactory apis, Func<IGatewayConnection> gateways,
    IVoiceMedia media, string defaultAddress)
{
    private readonly SemaphoreSlim _connection = new(1, 1);
    public Task<string?> SavedAddressAsync(CancellationToken token) =>
        cache.GetSettingAsync("workspace:address", token);
    public async Task<string> StartupAddressAsync(CancellationToken token) =>
        await SavedAddressAsync(token) ?? defaultAddress;

    public string ResolveAddress(string address) => WorkspaceAddress.Resolve(address, defaultAddress);

    public async Task<InstanceContext> ConnectAsync(string address, CancellationToken token)
    {
        address = ResolveAddress(address);
        await _connection.WaitAsync(token);
        try
        {
            var context = await instances.AddAsync(address, token, updateSavedAddress: true);
            if (context.Session is null)
            {
                var discovery = await instances.RefreshDiscoveryAsync(context, token);
                var api = apis.Create(discovery.Api, discovery.Gateway);
                InstanceSession? session = null;
                try
                {
                    var existingAccount = await cache.GetSettingAsync("account:" + context.Descriptor.Id.Value, token);
                    session = await InstanceSession.RestoreAsync(context.Descriptor, api, cache, vault, gateways, media, token);
                    if (session is null)
                    {
                        // Never silently replace an existing identity after an expired session or lost credential.
                        if (existingAccount is not null) throw new ClientFault(TextKey.SessionRecoveryRequired);
                        var username = "user_" + Guid.NewGuid().ToString("N")[..12];
                        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                        session = await InstanceSession.SignInAsync(context.Descriptor, api, cache, vault,
                            gateways, media, username, password, username, true, token);
                    }
                    session.ApplyDiscovery(discovery);
                    context.AttachSession(session);
                }
                catch
                {
                    if (session is not null) await session.DisposeAsync();
                    else api.Dispose();
                    throw;
                }
            }
            await context.Session!.RefreshCommunityAsync(token);
            if (LocalNetwork.IsTrustedDevelopmentHost(context.Descriptor.BaseUrl) && context.Session.Servers.Count == 0)
                await context.Session.CreateServerAsync(context.Descriptor.DisplayName, token);
            await cache.SetSettingAsync("workspace:address", context.Descriptor.BaseUrl.AbsoluteUri, token);
            return context;
        }
        finally { _connection.Release(); }
    }

    public async Task DisconnectAsync(InstanceContext context, CancellationToken token)
    {
        await _connection.WaitAsync(token);
        try
        {
            if (context.Session is { } session)
            {
                await session.DisposeAsync();
                context.AttachSessionClear();
            }
        }
        finally { _connection.Release(); }
    }
}
