namespace Chat.Domain.Instances;

public readonly record struct InstanceId(Guid Value);
public readonly record struct EntityKey(InstanceId InstanceId, Guid Id);
public sealed record InstanceDescriptor(InstanceId Id, Uri BaseUrl, string DisplayName);
public sealed record Account(EntityKey Key, string DisplayName);
