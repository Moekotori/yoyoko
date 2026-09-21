using Chat.Domain.Instances;
using Chat.Localization;
using Chat.Protocol;

namespace Chat.Core.Instances;

public sealed class InstanceManager(IInstanceStore store, IInstanceDiscovery discovery) : IAsyncDisposable
{
    private readonly Dictionary<InstanceId, InstanceContext> _contexts = [];
    public IReadOnlyCollection<InstanceContext> Contexts => _contexts.Values;

    public async Task LoadCachedAsync(CancellationToken cancellationToken)
    {
        foreach (var item in await store.LoadAsync(cancellationToken))
            _contexts.TryAdd(item.Id, new InstanceContext(item));
    }

    public async Task<InstanceDiscovery> RefreshDiscoveryAsync(InstanceContext context, CancellationToken cancellationToken)
    {
        var info = await discovery.DiscoverAsync(context.Descriptor.BaseUrl, cancellationToken);
        context.AttachDiscovery(info);
        return info;
    }

    public async Task<InstanceContext> AddAsync(string address, CancellationToken cancellationToken, bool updateSavedAddress = false)
    {
        var uri = NormalizeAddress(address);
        var info = await discovery.DiscoverAsync(uri, cancellationToken);
        if (info.ProtocolVersion != ProtocolVersion.Current || info.ApiVersion != 1)
            throw new ClientFault(TextKey.ProtocolIncompatible);
        var id = new InstanceId(info.InstanceId);
        if (_contexts.TryGetValue(id, out var existing))
        {
            if (existing.Descriptor.BaseUrl == uri) return existing;
            // Only an explicitly configured startup address may replace a saved alias before login.
            if (!updateSavedAddress || existing.Session is not null)
                throw new ClientFault(TextKey.InstanceAddressConflict);
        }
        if (existing is null && _contexts.Count >= 32) throw new ClientFault(TextKey.TooManyInstances);
        var descriptor = new InstanceDescriptor(id, uri, info.Name);
        await store.SaveAsync(descriptor, cancellationToken);
        var context = new InstanceContext(descriptor);
        context.AttachDiscovery(info);
        _contexts[id] = context;
        return context;
    }

    public static Uri NormalizeAddress(string address)
    {
        address = address.Trim();
        if (!address.Contains("://", StringComparison.Ordinal))
        {
            var host = HostOfUnprefixed(address);
            address = (LocalNetwork.IsTrustedDevelopmentHostName(host) ? "http://" : "https://") + address;
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !LocalNetwork.AllowsCleartext(uri)) ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ClientFault(TextKey.InvalidInstanceAddress);
        return uri;
    }

    static string HostOfUnprefixed(string address)
    {
        if (address.StartsWith('['))
        {
            var end = address.IndexOf(']');
            return end > 1 ? address[1..end] : address;
        }
        var slash = address.IndexOf('/');
        if (slash >= 0) address = address[..slash];
        var colon = address.IndexOf(':');
        return colon < 0 ? address : address[..colon];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts.Values) await context.DisposeAsync();
        _contexts.Clear();
    }
}
