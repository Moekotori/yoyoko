using Chat.Localization;

namespace Chat.Core.Instances;

public static class WorkspaceAddress
{
    public const string LocalServerUrl = "http://localhost:8080";
    // An application shortcut only. Explicit ports and other hosts keep their normal meaning.
    public static string Resolve(string address, string defaultAddress)
    {
        if (IsDefaultAlias(address))
        {
            if (string.IsNullOrWhiteSpace(defaultAddress) || IsDefaultAlias(defaultAddress))
                throw new ClientFault(TextKey.InvalidInstanceAddress);
            address = defaultAddress;
        }
        return InstanceManager.NormalizeAddress(address).AbsoluteUri;
    }

    private static bool IsDefaultAlias(string address)
    {
        var value = address.Trim().TrimEnd('/');
        return value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Equals("http://localhost", StringComparison.OrdinalIgnoreCase);
    }
}
