using Chat.Core.Instances;

namespace Chat.App;

internal sealed class DefaultWorkspace(WorkspaceConnection connection)
{
    public async Task<InstanceContext?> PrepareAsync(CancellationToken token)
    {
        var address = await connection.StartupAddressAsync(token);
        return string.IsNullOrWhiteSpace(address) ? null : await connection.ConnectAsync(address, token);
    }
}
