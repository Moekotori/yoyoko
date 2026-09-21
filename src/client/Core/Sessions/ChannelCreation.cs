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

    public async Task<ChannelDto> OpenDirectAsync(Guid recipientId, CancellationToken cancellationToken)
    {
        var channel = await _api.OpenDirectAsync(recipientId, cancellationToken);
        await RefreshCommunityAsync(cancellationToken);
        return channel;
    }

    public string ChannelTitle(ChannelDto channel)
    {
        if (channel.Kind != "dm") return channel.Name;
        var peer = channel.Participants?.FirstOrDefault(id => id != Me.Id) ?? Guid.Empty;
        return peer == Guid.Empty ? channel.Name : AuthorName(peer);
    }
}
