using Chat.Domain.Instances;
using Chat.Protocol;

namespace Chat.Core.Instances;

public interface IInstanceDiscovery
{
    Task<InstanceDiscovery> DiscoverAsync(Uri baseUrl, CancellationToken cancellationToken);
}
public interface IInstanceStore
{
    Task<IReadOnlyList<InstanceDescriptor>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(InstanceDescriptor instance, CancellationToken cancellationToken);
}
// Secrets belong in the OS credential store, never SQLite or configuration.
// Not implemented yet: Keychain / Credential Manager / Secret Service adapters.
public interface ICredentialVault
{
    Task<string?> GetAsync(InstanceId instance, Guid accountId, CancellationToken cancellationToken);
    Task StoreAsync(InstanceId instance, Guid accountId, string refreshToken, CancellationToken cancellationToken);
    Task RemoveAsync(InstanceId instance, Guid accountId, CancellationToken cancellationToken);
}
