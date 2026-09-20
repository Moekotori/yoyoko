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
public sealed record UserDto(Guid Id, string Username, string DisplayName);
public sealed record RegisterRequest(string Username, string DisplayName, string Password);
public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, string RefreshToken, ulong ExpiresIn, UserDto User);
public sealed record ServerDto(Guid Id, string Name, Guid OwnerId, string InviteCode);
public sealed record ChannelDto(Guid Id, Guid ServerId, string Name, string Kind);
public sealed record CreateServerRequest(string Name);
public sealed record CreateChannelRequest(string Name, string Kind);
public sealed record JoinRequest(string InviteCode);
public sealed record VoiceFlags(bool SelfMute, bool SelfDeaf);
public sealed record VoiceStateDto(Guid UserId, Guid ServerId, Guid? ChannelId, bool SelfMute, bool SelfDeaf, string DisplayName);
public sealed record RtcTokenDto(string Token, Uri Url, string Room, DateTimeOffset ExpiresAt);
public sealed record VoiceJoinDto(RtcTokenDto Rtc, VoiceStateDto State);
public sealed record ReadyDto(string SessionId, UserDto User, ServerDto[] Servers, ChannelDto[] Channels,
    UserDto[] Users, int HeartbeatIntervalMs, VoiceStateDto[]? VoiceStates);
public sealed record SendMessageRequest(string? Content, Guid? ReplyTo, Guid[] AttachmentIds);
public sealed record MessagePageDto(MessageDto[] Items, Guid? Before);
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
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(ServerDto))]
[JsonSerializable(typeof(ServerDto[]))]
[JsonSerializable(typeof(ChannelDto))]
[JsonSerializable(typeof(ChannelDto[]))]
[JsonSerializable(typeof(CreateServerRequest))]
[JsonSerializable(typeof(CreateChannelRequest))]
[JsonSerializable(typeof(JoinRequest))]
[JsonSerializable(typeof(VoiceFlags))]
[JsonSerializable(typeof(VoiceStateDto))]
[JsonSerializable(typeof(VoiceStateDto[]))]
[JsonSerializable(typeof(RtcTokenDto))]
[JsonSerializable(typeof(VoiceJoinDto))]
[JsonSerializable(typeof(ReadyDto))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(MessagePageDto))]
[JsonSerializable(typeof(MessageDto))]
[JsonSerializable(typeof(MessageDto[]))]
[JsonSerializable(typeof(Guid[]))]
public partial class ProtocolJson : JsonSerializerContext;
