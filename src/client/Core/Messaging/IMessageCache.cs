using Chat.Domain.Instances;
using Chat.Protocol;

namespace Chat.Core.Messaging;

public readonly record struct CacheScope(InstanceId InstanceId, Guid AccountId);
public sealed record MessagePage(IReadOnlyList<MessageDto> Items, Guid? Before);
public sealed record CommunitySnapshot(IReadOnlyList<ServerDto> Servers, IReadOnlyList<ChannelDto> Channels, IReadOnlyList<UserDto> Users);
public interface IMessageCache
{
    Task UpsertAsync(CacheScope scope, MessageDto message, CancellationToken cancellationToken);
    Task<MessagePage> ReadPageAsync(CacheScope scope, Guid channelId, Guid? before, int limit,
        CancellationToken cancellationToken);
    Task PurgeAsync(CacheScope scope, CancellationToken cancellationToken);
    Task SaveCommunityAsync(CacheScope scope, CommunitySnapshot snapshot, CancellationToken cancellationToken);
    Task<CommunitySnapshot> LoadCommunityAsync(CacheScope scope, CancellationToken cancellationToken);
    Task SaveCursorAsync(CacheScope scope, string? sessionId, long seq, CancellationToken cancellationToken);
    Task<(string? SessionId, long Seq)> LoadCursorAsync(CacheScope scope, CancellationToken cancellationToken);
    Task SaveAccountAsync(CacheScope scope, string displayName, CancellationToken cancellationToken);
    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken);
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelInbox>> LoadInboxAsync(CacheScope scope, CancellationToken cancellationToken);
    Task NoteArrivalAsync(CacheScope scope, MessageDto message, bool mentioned, CancellationToken cancellationToken);
    Task SaveReadAsync(CacheScope scope, Guid channelId, Guid lastReadId, CancellationToken cancellationToken);
    Task SaveNotifyAsync(CacheScope scope, Guid channelId, ChannelNotify notify, CancellationToken cancellationToken);
    Task SaveDraftAsync(CacheScope scope, Guid channelId, string? draft, CancellationToken cancellationToken);
}
public static class MemoryBudget
{
    public const int TimelineMessages = 300;
    public const int PageSize = 50;
    public const long ThumbnailBytes = 16 * 1024 * 1024;
    public const int VideoFrames = 3;
    public const long CacheDiskBytes = 256 * 1024 * 1024;
}
