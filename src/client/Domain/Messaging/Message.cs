using Chat.Domain.Instances;

namespace Chat.Domain.Messaging;

public enum MessageKind { Text, System, Encrypted }
public sealed record Attachment(Guid Id, string FileName, string MimeType, long Size,
    Uri DownloadUrl, Uri? ThumbnailUrl);
public sealed record Embed(string? Title, string? Description, Uri? Url);
public sealed record Reaction(string Emoji, int Count, bool ReactedByAccount);
public sealed record EncryptedPayload(string Algorithm, string KeyId, string Ciphertext);
public sealed record Message(EntityKey Key, Guid ChannelId, Guid AuthorId, MessageKind Kind,
    string? Content, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, Guid? ReplyTo,
    IReadOnlyList<Guid> Mentions, IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<Embed> Embeds, IReadOnlyList<Reaction> Reactions, EncryptedPayload? Encrypted);
