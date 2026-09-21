using Chat.Protocol;

namespace Chat.Core.Sessions;

public sealed partial class InstanceSession
{
    public async Task<ChannelDto> CreateChannelAsync(Guid serverId, string name, string kind,
        CancellationToken cancellationToken)
    {
        var channel = await _api.CreateChannelAsync(serverId, new(name.Trim(), kind, null), cancellationToken);
        await RefreshCommunityAsync(cancellationToken);
        return channel;
    }
}
