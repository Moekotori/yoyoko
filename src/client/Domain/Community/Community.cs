using Chat.Domain.Instances;

namespace Chat.Domain.Community;

public sealed record Server(EntityKey Key, string Name, Guid OwnerId);
public enum ChannelKind { Text, Voice }
public sealed record Channel(EntityKey Key, Guid ServerId, string Name, ChannelKind Kind);
public sealed record Member(EntityKey User, Guid ServerId, IReadOnlyList<Guid> RoleIds);
public sealed record Role(EntityKey Key, Guid ServerId, string Name, Permissions.Permission Permissions);
