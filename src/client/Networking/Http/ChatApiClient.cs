using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Chat.Core.Api;
using Chat.Protocol;

namespace Chat.Networking.Http;

public sealed class ChatApiClient(HttpClient client) : IChatApi
{
    public Task<AuthResponse> RegisterAsync(Uri api, RegisterRequest request, CancellationToken cancellationToken)
        => Send(api, HttpMethod.Post, "auth/register", null, request, ProtocolJson.Default.RegisterRequest, ProtocolJson.Default.AuthResponse, cancellationToken)!;
    public Task<AuthResponse> LoginAsync(Uri api, LoginRequest request, CancellationToken cancellationToken)
        => Send(api, HttpMethod.Post, "auth/login", null, request, ProtocolJson.Default.LoginRequest, ProtocolJson.Default.AuthResponse, cancellationToken)!;
    public Task LogoutAsync(Uri api, string accessToken, CancellationToken cancellationToken)
        => Send<object, object>(api, HttpMethod.Post, "auth/logout", accessToken, null, null, null, cancellationToken);
    public Task<ServerDto> CreateServerAsync(Uri api, string accessToken, string name, CancellationToken cancellationToken)
        => Send(api, HttpMethod.Post, "servers", accessToken, new CreateServerRequest(name), ProtocolJson.Default.CreateServerRequest, ProtocolJson.Default.ServerDto, cancellationToken)!;
    public async Task<IReadOnlyList<ServerDto>> ListServersAsync(Uri api, string accessToken, CancellationToken cancellationToken)
        => await Send(api, HttpMethod.Get, "servers", accessToken, (object?)null, null, ProtocolJson.Default.ServerDtoArray, cancellationToken) ?? [];
    public async Task<IReadOnlyList<ChannelDto>> ListChannelsAsync(Uri api, string accessToken, Guid serverId, CancellationToken cancellationToken)
        => await Send(api, HttpMethod.Get, $"servers/{serverId}/channels", accessToken, (object?)null, null, ProtocolJson.Default.ChannelDtoArray, cancellationToken) ?? [];
    public Task<ServerDto> JoinServerAsync(Uri api, string accessToken, string inviteCode, CancellationToken cancellationToken)
        => Send(api, HttpMethod.Post, "servers/join", accessToken, new JoinRequest(inviteCode), ProtocolJson.Default.JoinRequest, ProtocolJson.Default.ServerDto, cancellationToken)!;
    public Task<VoiceJoinDto> JoinVoiceAsync(Uri api, string accessToken, Guid channelId, bool mute, bool deaf, CancellationToken cancellationToken)
        => Send(api, HttpMethod.Post, $"channels/{channelId}/voice/join", accessToken, new VoiceFlags(mute, deaf), ProtocolJson.Default.VoiceFlags, ProtocolJson.Default.VoiceJoinDto, cancellationToken)!;
    public Task LeaveVoiceAsync(Uri api, string accessToken, CancellationToken cancellationToken)
        => Send<object, object>(api, HttpMethod.Post, "voice/leave", accessToken, null, null, null, cancellationToken);
    public Task<VoiceStateDto> PatchVoiceAsync(Uri api, string accessToken, bool mute, bool deaf, CancellationToken cancellationToken)
        => Send(api, HttpMethod.Patch, "voice/state", accessToken, new VoiceFlags(mute, deaf), ProtocolJson.Default.VoiceFlags, ProtocolJson.Default.VoiceStateDto, cancellationToken)!;

    private async Task<TOut?> Send<TIn, TOut>(Uri api, HttpMethod method, string path, string? token, TIn? body,
        JsonTypeInfo<TIn>? input, JsonTypeInfo<TOut>? output, CancellationToken cancellationToken)
        where TIn : class
        where TOut : class
    {
        using var request = new HttpRequestMessage(method, Combine(api, path));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null && input is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, input), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await response.Content.LoadIntoBufferAsync(64 * 1024, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = JsonSerializer.Deserialize(json, ProtocolJson.Default.ApiError); } catch (JsonException) { }
            throw new ChatApiException(error?.Code ?? "http_error", error?.Message ?? $"HTTP {(int)response.StatusCode}");
        }
        if (output is null || response.StatusCode == HttpStatusCode.NoContent || json.Length == 0) return null;
        return JsonSerializer.Deserialize(json, output) ?? throw new ChatApiException("invalid_response", "Empty API response.");
    }

    private static Uri Combine(Uri api, string path)
    {
        var root = api.AbsoluteUri.TrimEnd('/') + "/";
        return new Uri(root + path.TrimStart('/'));
    }
}
