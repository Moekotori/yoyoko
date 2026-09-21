using System.Net;
using Chat.Core.Instances;

namespace Chat.Networking.Http;

public static class ClientHttp
{
    public static IWebProxy Proxy { get; } = new DevelopmentBypassProxy();

    public static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = true,
        Proxy = Proxy
    };

    private sealed class DevelopmentBypassProxy : IWebProxy
    {
        private static readonly IWebProxy SystemProxy = HttpClient.DefaultProxy;
        public ICredentials? Credentials
        {
            get => SystemProxy.Credentials;
            set => SystemProxy.Credentials = value;
        }
        public Uri? GetProxy(Uri destination) => IsBypassed(destination) ? null : SystemProxy.GetProxy(destination);
        public bool IsBypassed(Uri host) =>
            LocalNetwork.IsTrustedDevelopmentHost(host) || SystemProxy.IsBypassed(host);
    }
}
