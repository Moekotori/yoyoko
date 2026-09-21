namespace Chat.Core.Messaging;

public sealed record OutboxFile(string FileName, string MimeType, long Size, string? Path);

public sealed record OutboxRecord(
    Guid LocalId,
    Guid ChannelId,
    string Idempotency,
    string? Content,
    Guid? ReplyTo,
    DateTimeOffset CreatedAt,
    string Status,
    Guid[] Uploaded,
    OutboxFile[] Files);
