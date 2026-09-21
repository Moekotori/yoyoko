namespace Chat.Core.Messaging;

public enum ChannelNotify { All, Mentions, Mute }

public sealed record ChannelInbox(
    Guid ChannelId,
    Guid? LastReadId,
    Guid? LastMessageId,
    Guid? LastAuthorId,
    Guid? LastMentionId,
    ChannelNotify Notify,
    string? Draft)
{
    public static ChannelNotify ParseNotify(string? value) => value switch
    {
        "mentions" => ChannelNotify.Mentions,
        "mute" => ChannelNotify.Mute,
        _ => ChannelNotify.All
    };

    public static string NotifyId(ChannelNotify value) => value switch
    {
        ChannelNotify.Mentions => "mentions",
        ChannelNotify.Mute => "mute",
        _ => "all"
    };

    public bool HasUnread(Guid selfId) =>
        LastMessageId is Guid last
        && MessageMarkup.IdAfter(last, LastReadId)
        && LastAuthorId != selfId;

    public bool HasMention =>
        LastMentionId is Guid mention && MessageMarkup.IdAfter(mention, LastReadId);

    public bool ShowUnread(Guid selfId) =>
        Notify == ChannelNotify.All && HasUnread(selfId);

    public bool ShowMention => HasMention;

    public bool IsMuted => Notify == ChannelNotify.Mute;
}
