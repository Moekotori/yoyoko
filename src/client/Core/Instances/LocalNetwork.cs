using System.Net;
using System.Net.Sockets;

namespace Chat.Core.Instances;

public static class LocalNetwork
{
    public static bool IsTrustedDevelopmentHost(Uri uri) =>
        uri.IsLoopback || IsTrustedDevelopmentHostName(uri.IdnHost);

    public static bool IsTrustedDevelopmentHostName(string host)
    {
        host = host.Trim().TrimStart('[').TrimEnd(']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IsPrivateOrLoopback(ip);
    }

    public static bool AllowsCleartext(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == "ws") && IsTrustedDevelopmentHost(uri);

    static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }
}
