using System.Text.Json;
using Chat.Core.Instances;
using Chat.Protocol;

namespace Chat.Networking.Http;

public sealed class HttpInstanceDiscovery(HttpClient client) : IInstanceDiscovery
{
    public async Task<InstanceDiscovery> DiscoverAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(baseUrl, ProtocolVersion.DiscoveryPath),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        // Bounded discovery responses; remote configuration must not allocate without a limit.
        await response.Content.LoadIntoBufferAsync(16 * 1024, cancellationToken);
        var info = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(cancellationToken),
            ProtocolJson.Default.InstanceDiscovery) ?? throw new InvalidDataException("实例发现响应为空。");
        if (info.InstanceId == Guid.Empty || string.IsNullOrWhiteSpace(info.Name) || info.Name.Length > 100)
            throw new InvalidDataException("实例身份无效。");
        ValidateEndpoint(info.Api, baseUrl, false);
        ValidateEndpoint(info.Gateway, baseUrl, true);
        ValidateEndpoint(info.Cdn, baseUrl, false);
        ValidateEndpoint(info.Rtc, baseUrl, false);
        // Never send account credentials to a different origin named by discovery.
        if (info.Api.Authority != baseUrl.Authority || info.Gateway.Authority != baseUrl.Authority)
            throw new InvalidDataException("API 和 Gateway 必须与实例同源。");
        return info;
    }

    private static void ValidateEndpoint(Uri endpoint, Uri origin, bool websocket)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0 ||
            !(endpoint.Scheme == (websocket ? "wss" : "https") ||
              (origin.IsLoopback && endpoint.IsLoopback && endpoint.Scheme == (websocket ? "ws" : "http"))))
            throw new InvalidDataException("实例端点必须使用安全连接。");
    }
}
