using Chat.Protocol;

namespace Chat.Core.Sessions;

public sealed record PickedFile(string FileName, string MimeType, Stream Content, long Size) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        Content.Dispose();
        return ValueTask.CompletedTask;
    }
}

public interface IChatApi : IDisposable
{
    Uri ApiBase { get; }
    Uri Gateway { get; }
    void SetAccessToken(string? token);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
    Task<UserDto> GetMeAsync(CancellationToken cancellationToken);
    Task<UserDto> PatchMeAsync(PatchMeRequest request, CancellationToken cancellationToken);
    Task<ServerDto> CreateServerAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServerDto>> ListServersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelDto>> ListChannelsAsync(Guid serverId, CancellationToken cancellationToken);
    Task<ServerDto> JoinAsync(string inviteCode, CancellationToken cancellationToken);
    Task<MessagePageDto> ListMessagesAsync(Guid channelId, Guid? before, int limit, CancellationToken cancellationToken);
    Task<MessageDto> SendMessageAsync(Guid channelId, SendMessageRequest request, string idempotencyKey, CancellationToken cancellationToken);
    Task<AttachmentDto> UploadAsync(PickedFile file, CancellationToken cancellationToken);
    Task<byte[]?> DownloadAsync(Uri url, int maxBytes, CancellationToken cancellationToken);
    Task DownloadToAsync(Uri url, Stream destination, long maxBytes, CancellationToken cancellationToken);
    Task<VoiceJoinDto> JoinVoiceAsync(Guid channelId, bool mute, bool deaf, string? quality, CancellationToken cancellationToken);
    Task LeaveVoiceAsync(CancellationToken cancellationToken);
    Task<VoiceStateDto> PatchVoiceAsync(bool mute, bool deaf, string? quality, CancellationToken cancellationToken);
    Task<ChannelDto> PatchChannelAsync(Guid channelId, PatchChannelRequest request, CancellationToken cancellationToken);
    Task<ServerDto> PatchModerationAsync(Guid serverId, PatchModerationRequest request, CancellationToken cancellationToken);
}

public sealed class ChatApiException(string code, string message, int? retryAfterSeconds = null) : Exception(message)
{
    public string Code { get; } = code;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public interface IChatApiFactory
{
    IChatApi Create(Uri api, Uri gateway);
}
