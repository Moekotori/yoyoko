using Chat.Protocol;

namespace Chat.Core.Api;

public interface IChatApi
{
    Task<AuthResponse> RegisterAsync(Uri api, RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(Uri api, LoginRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(Uri api, string accessToken, CancellationToken cancellationToken);
    Task<ServerDto> CreateServerAsync(Uri api, string accessToken, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServerDto>> ListServersAsync(Uri api, string accessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChannelDto>> ListChannelsAsync(Uri api, string accessToken, Guid serverId, CancellationToken cancellationToken);
    Task<ServerDto> JoinServerAsync(Uri api, string accessToken, string inviteCode, CancellationToken cancellationToken);
    Task<VoiceJoinDto> JoinVoiceAsync(Uri api, string accessToken, Guid channelId, bool mute, bool deaf, CancellationToken cancellationToken);
    Task LeaveVoiceAsync(Uri api, string accessToken, CancellationToken cancellationToken);
    Task<VoiceStateDto> PatchVoiceAsync(Uri api, string accessToken, bool mute, bool deaf, CancellationToken cancellationToken);
}

public sealed class ChatApiException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
