using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chat.Protocol;

public static class ProtocolVersion
{
    public const int Current = 1;
    public const string DiscoveryPath = "/.well-known/lightchat";
    public const int MaxPageSize = 100;
    public const int MaxGatewayBytes = 65_536;
}

public sealed record InstanceDiscovery(Guid InstanceId, string Name, int ProtocolVersion,
    int ApiVersion, Uri Api, Uri Gateway, Uri Cdn, Uri Rtc);
public sealed record GatewayEnvelope(string Op, string? Event, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? Seq, JsonElement Data);
public sealed record GatewayHello(int ProtocolVersion, int HeartbeatIntervalMs);
public sealed record GatewayIdentify(int ProtocolVersion, string AccessToken);
public sealed record GatewayResume(int ProtocolVersion, string AccessToken, string SessionId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long LastSeq);
public sealed record ApiError(string Code, string Message);
// Permission bitsets and gateway sequence counters are decimal strings on the wire.
public sealed record MessageDto(Guid Id, Guid ChannelId, Guid AuthorId, string Kind,
    string? Content, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, Guid? ReplyTo,
    Guid[] Mentions, AttachmentDto[] Attachments, JsonElement[] Embeds,
    JsonElement[] Reactions, JsonElement? EncryptedPayload);
public sealed record AttachmentDto(Guid Id, string FileName, string MimeType, long Size,
    Uri DownloadUrl, Uri? ThumbnailUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(InstanceDiscovery))]
[JsonSerializable(typeof(GatewayEnvelope))]
[JsonSerializable(typeof(GatewayHello))]
[JsonSerializable(typeof(GatewayIdentify))]
[JsonSerializable(typeof(GatewayResume))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(MessageDto))]
[JsonSerializable(typeof(MessageDto[]))]
public partial class ProtocolJson : JsonSerializerContext;
