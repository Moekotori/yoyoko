namespace Chat.UI.Chat;

// Local layout names only. Not membership, presence, or anything sent to Core/network.
internal static class MemberFixtures
{
    public static readonly (Guid Id, string Name, string Username, bool Online)[] Seeds =
    [
        (Guid.Parse("01900000-0000-7000-8000-000000000011"), "Mika", "mika", true),
        (Guid.Parse("01900000-0000-7000-8000-000000000012"), "苏晚", "suwan", true),
        (Guid.Parse("01900000-0000-7000-8000-000000000015"), "Nova", "nova", true),
        (Guid.Parse("01900000-0000-7000-8000-000000000016"), "林栖迟", "linqichi", true),
        (Guid.Parse("01900000-0000-7000-8000-000000000017"), "Alexander Whitfield", "alexander", true),
        (Guid.Parse("01900000-0000-7000-8000-000000000013"), "陈默", "chenmo", false),
        (Guid.Parse("01900000-0000-7000-8000-000000000014"), "江河", "jianghe", false),
        (Guid.Parse("01900000-0000-7000-8000-000000000018"), "月见里", "tsukimi", false),
        (Guid.Parse("01900000-0000-7000-8000-000000000019"), "Ryo", "ryo", false),
        (Guid.Parse("01900000-0000-7000-8000-00000000001a"), "阿布杜勒·拉赫曼", "abdurrahman", false),
    ];
}
