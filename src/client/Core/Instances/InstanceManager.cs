using Chat.Domain.Instances;
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

    public async Task<InstanceContext> AddAsync(string address, CancellationToken cancellationToken)
    {
        var uri = NormalizeAddress(address);
        var info = await discovery.DiscoverAsync(uri, cancellationToken);
        if (info.ProtocolVersion != ProtocolVersion.Current || info.ApiVersion != 1)
            throw new InvalidOperationException("实例协议版本不兼容。");
        var id = new InstanceId(info.InstanceId);
        if (_contexts.TryGetValue(id, out var existing))
        {
            if (existing.Descriptor.BaseUrl != uri) throw new InvalidOperationException("此实例已通过另一个地址添加。");
            return existing;
        }
        if (_contexts.Count >= 32) throw new InvalidOperationException("最多可添加 32 个实例。");
        var descriptor = new InstanceDescriptor(id, uri, info.Name);
        await store.SaveAsync(descriptor, cancellationToken);
        var context = new InstanceContext(descriptor);
        _contexts.Add(id, context);
        return context;
    }

    public static Uri NormalizeAddress(string address)
    {
        address = address.Trim();
        if (!address.Contains("://", StringComparison.Ordinal)) address = "https://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("请输入实例域名或 HTTPS 地址；本机开发可使用 HTTP。");
        return uri;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts.Values) await context.DisposeAsync();
        _contexts.Clear();
    }
}
