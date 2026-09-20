using Chat.Protocol;

namespace Chat.Core.Sessions;

public sealed record PickedImage(string FileName, string MimeType, Stream Content, long Size) : IAsyncDisposable
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
    Task<ServerDto> CreateServerAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServerDto>> ListServersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelDto>> ListChannelsAsync(Guid serverId, CancellationToken cancellationToken);
    Task<ServerDto> JoinAsync(string inviteCode, CancellationToken cancellationToken);
    Task<MessagePageDto> ListMessagesAsync(Guid channelId, Guid? before, int limit, CancellationToken cancellationToken);
    Task<MessageDto> SendMessageAsync(Guid channelId, SendMessageRequest request, string idempotencyKey, CancellationToken cancellationToken);
    Task<AttachmentDto> UploadAsync(PickedImage image, CancellationToken cancellationToken);
    Task<byte[]?> DownloadAsync(Uri url, int maxBytes, CancellationToken cancellationToken);
    Task<VoiceJoinDto> JoinVoiceAsync(Guid channelId, bool mute, bool deaf, CancellationToken cancellationToken);
    Task LeaveVoiceAsync(CancellationToken cancellationToken);
    Task<VoiceStateDto> PatchVoiceAsync(bool mute, bool deaf, CancellationToken cancellationToken);
}

public sealed class ChatApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IChatApiFactory
{
    IChatApi Create(Uri api, Uri gateway);
}
