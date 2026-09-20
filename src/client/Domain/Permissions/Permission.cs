namespace Chat.Domain.Permissions;

[Flags]
public enum Permission : ulong
{
    None = 0, ViewChannel = 1UL << 0, SendMessage = 1UL << 1,
    ManageMessages = 1UL << 2, ConnectVoice = 1UL << 3, Speak = 1UL << 4,
    Stream = 1UL << 5, ManageChannel = 1UL << 6, ManageRole = 1UL << 7,
    KickMember = 1UL << 8, BanMember = 1UL << 9, Administrator = 1UL << 10
}

public readonly record struct PermissionOverride(Permission Allow, Permission Deny);

public static class PermissionResolver
{
    // Aggregate role overrides before applying; explicit member override wins last.
    public static Permission Resolve(Permission baseRole, IEnumerable<Permission> roles,
        PermissionOverride everyone, IEnumerable<PermissionOverride> roleOverrides,
        PermissionOverride member)
    {
        var result = roles.Aggregate(baseRole, (current, role) => current | role);
        if (result.HasFlag(Permission.Administrator)) return (Permission)ulong.MaxValue;
        result = Apply(result, everyone);
        var allow = Permission.None;
        var deny = Permission.None;
        foreach (var item in roleOverrides) { allow |= item.Allow; deny |= item.Deny; }
        return Apply(Apply(result, new(allow, deny)), member);
    }

    private static Permission Apply(Permission value, PermissionOverride change)
        => (value & ~change.Deny) | change.Allow;
}
