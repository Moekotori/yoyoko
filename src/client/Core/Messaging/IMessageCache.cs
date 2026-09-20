using Chat.Domain.Instances;
using Chat.Protocol;

namespace Chat.Core.Messaging;

// Account scope prevents cached private content leaking when switching accounts.
public readonly record struct CacheScope(InstanceId InstanceId, Guid AccountId);
public sealed record MessagePage(IReadOnlyList<MessageDto> Items, Guid? Before);
public interface IMessageCache
{
    Task UpsertAsync(CacheScope scope, MessageDto message, CancellationToken cancellationToken);
    Task<MessagePage> ReadPageAsync(CacheScope scope, Guid channelId, Guid? before, int limit,
        CancellationToken cancellationToken);
    Task PurgeAsync(CacheScope scope, CancellationToken cancellationToken);
}
public static class MemoryBudget
{
    public const int TimelineMessages = 300;
    public const int PageSize = 50;
    public const long ThumbnailBytes = 16 * 1024 * 1024;
    public const int VideoFrames = 3;
    public const long CacheDiskBytes = 256 * 1024 * 1024;
}
