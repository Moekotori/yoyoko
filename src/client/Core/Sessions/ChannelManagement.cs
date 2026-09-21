namespace Chat.Core.Sessions;

public sealed partial class InstanceSession
{
    public async Task RenameChannelAsync(Guid channelId, string name, CancellationToken cancellationToken)
    {
        await _api.PatchChannelAsync(channelId, new(Name: name.Trim()), cancellationToken);
        await RefreshCommunityAsync(cancellationToken);
    }
    public async Task DeleteChannelAsync(Guid channelId, CancellationToken cancellationToken)
    {
        await _api.DeleteChannelAsync(channelId, cancellationToken);
        await RefreshCommunityAsync(cancellationToken);
    }
}
