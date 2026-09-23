using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chat.Protocol;

public static class ProtocolVersion
{
    public const int Current = 1;
    public const string DiscoveryPath = "/.well-known/lightchat";
    public const string HealthLivePath = "/health/live";
    public const int MaxPageSize = 100;
    public const int MaxGatewayBytes = 65_536;
    public const long MaxAttachmentBytes = 25_165_824;
    public const int MaxAttachmentsPerMessage = 4;
    public const long MaxAvatarBytes = 8_388_608;
}

public sealed record InstanceDiscovery(Guid InstanceId, string Name, int ProtocolVersion,
    int ApiVersion, Uri Api, Uri Gateway, Uri Cdn, Uri Rtc, long MaxAttachmentBytes,
    int MaxAttachmentsPerMessage);
public sealed record GatewayEnvelope(string Op, string? Event, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long? Seq, JsonElement Data);
public sealed record GatewayHello(int ProtocolVersion, int HeartbeatIntervalMs);
public sealed record GatewayIdentify(int ProtocolVersion, string AccessToken);
public sealed record GatewayResume(int ProtocolVersion, string AccessToken, string SessionId, [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long LastSeq);
public sealed record ApiError(string Code, string Message, int? RetryAfterSeconds = null);
public sealed record AvatarDto(Guid Id, string MimeType, long Size, Uri DownloadUrl, Uri? ThumbnailUrl, bool Animated);
public sealed record UserDto(Guid Id, string Username, string DisplayName, AvatarDto? Avatar = null,
    AvatarDto? Banner = null);
public sealed record PatchMeRequest(string? Username, string? DisplayName, Guid? AvatarId, bool ClearAvatar = false,
    Guid? BannerId = null, bool ClearBanner = false);
public sealed record RegisterRequest(string Username, string DisplayName, string Password,
    string? ServerPassword = null);
public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record AuthResponse(string AccessToken, string RefreshToken, ulong ExpiresIn, UserDto User);
public sealed record ServerDto(Guid Id, string Name, Guid OwnerId, string InviteCode,
    string[]? BlockedWords = null, int CooldownSeconds = 0);
public sealed record PatchModerationRequest(string[]? BlockedWords, int? CooldownSeconds);
public sealed record ChannelDto(Guid Id, Guid? ServerId, string Name, string Kind, string? AudioQuality,
    Guid[]? Participants = null);
public sealed record CreateServerRequest(string Name);
public sealed record CreateChannelRequest(string Name, string Kind, string? AudioQuality);
public sealed record OpenDmRequest(Guid RecipientId);
public sealed record PatchChannelRequest(string? AudioQuality = null, string? Name = null);
public sealed record JoinRequest(string InviteCode);
public sealed record VoiceFlags(bool SelfMute, bool SelfDeaf, string? AudioQuality);
public sealed record AudioProfileDto(string Id, int SampleRateHz, int Channels, int BitrateBps, int FrameMs, bool Dtx, bool Fec);
public sealed record VoiceStateDto(Guid UserId, Guid ServerId, Guid? ChannelId, bool SelfMute, bool SelfDeaf, string DisplayName, string AudioQuality);
public sealed record RtcTokenDto(string Token, Uri Url, string Room, DateTimeOffset ExpiresAt);
public sealed record VoiceJoinDto(RtcTokenDto Rtc, VoiceStateDto State, AudioProfileDto Audio, string MaxAudioQuality);
public sealed record VoiceRoomDto(Guid ChannelId, Guid ServerId, string Name, string ServerName, string Kind, uint ParticipantCount);
public sealed record VoiceGuestRequest(string DisplayName, bool SelfMute = false, bool SelfDeaf = false);
public sealed record VoiceGuestSessionDto(string AccessToken, ulong ExpiresIn, UserDto User, VoiceRoomDto Room, VoiceJoinDto Join);
public sealed record ReadyDto(string SessionId, UserDto User, ServerDto[] Servers, ChannelDto[] Channels,
    UserDto[] Users, int HeartbeatIntervalMs, VoiceStateDto[]? VoiceStates);
public sealed record SendMessageRequest(string? Content, Guid? ReplyTo, Guid[] AttachmentIds);
public sealed record PatchMessageRequest(string? Content);
public sealed record MessagePageDto(MessageDto[] Items, Guid? Before);
public sealed record MessageDto(Guid Id, Guid ChannelId, Guid AuthorId, string Kind,
    string? Content, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, Guid? ReplyTo,
    Guid[] Mentions, AttachmentDto[] Attachments, JsonElement[] Embeds,
    JsonElement[] Reactions, JsonElement? EncryptedPayload, bool MentionEveryone = false,
    bool MentionHere = false);
public sealed record MessageDeleteDto(Guid Id, Guid ChannelId);
public sealed record TypingDto(Guid UserId, Guid ChannelId, string DisplayName);
public sealed record AttachmentDto(Guid Id, string FileName, string MimeType, long Size,
    Uri DownloadUrl, Uri? ThumbnailUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(InstanceDiscovery))]
[JsonSerializable(typeof(GatewayEnvelope))]
[JsonSerializable(typeof(GatewayHello))]
[JsonSerializable(typeof(GatewayIdentify))]
[JsonSerializable(typeof(GatewayResume))]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(AvatarDto))]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(UserDto[]))]
[JsonSerializable(typeof(PatchMeRequest))]
[JsonSerializable(typeof(AttachmentDto))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(ServerDto))]
[JsonSerializable(typeof(ServerDto[]))]
[JsonSerializable(typeof(PatchModerationRequest))]
[JsonSerializable(typeof(ChannelDto))]
[JsonSerializable(typeof(ChannelDto[]))]
[JsonSerializable(typeof(CreateServerRequest))]
[JsonSerializable(typeof(CreateChannelRequest))]
[JsonSerializable(typeof(OpenDmRequest))]
[JsonSerializable(typeof(PatchChannelRequest))]
[JsonSerializable(typeof(JoinRequest))]
[JsonSerializable(typeof(VoiceFlags))]
[JsonSerializable(typeof(AudioProfileDto))]
[JsonSerializable(typeof(VoiceStateDto))]
[JsonSerializable(typeof(VoiceStateDto[]))]
[JsonSerializable(typeof(RtcTokenDto))]
[JsonSerializable(typeof(VoiceJoinDto))]
[JsonSerializable(typeof(VoiceRoomDto))]
[JsonSerializable(typeof(VoiceGuestRequest))]
[JsonSerializable(typeof(VoiceGuestSessionDto))]
[JsonSerializable(typeof(ReadyDto))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(PatchMessageRequest))]
[JsonSerializable(typeof(MessagePageDto))]
[JsonSerializable(typeof(MessageDto))]
[JsonSerializable(typeof(MessageDto[]))]
[JsonSerializable(typeof(MessageDeleteDto))]
[JsonSerializable(typeof(TypingDto))]
[JsonSerializable(typeof(Guid[]))]
public partial class ProtocolJson : JsonSerializerContext;
