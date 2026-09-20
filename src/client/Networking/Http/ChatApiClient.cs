using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Chat.Core.Sessions;
using Chat.Protocol;

namespace Chat.Networking.Http;

public sealed class ChatApiFactory : IChatApiFactory
{
    public IChatApi Create(Uri api, Uri gateway) => new ChatApiClient(api, gateway);
}

public sealed class ChatApiClient : IChatApi
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    private string? _token;
    public ChatApiClient(Uri api, Uri gateway)
    {
        ApiBase = api;
        Gateway = gateway;
    }
    public Uri ApiBase { get; }
    public Uri Gateway { get; }
    public void SetAccessToken(string? token) => _token = token;

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
        => Send(HttpMethod.Post, "auth/register", request, ProtocolJson.Default.RegisterRequest, ProtocolJson.Default.AuthResponse, cancellationToken)!;
    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        => Send(HttpMethod.Post, "auth/login", request, ProtocolJson.Default.LoginRequest, ProtocolJson.Default.AuthResponse, cancellationToken)!;
    public Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        => Send(HttpMethod.Post, "auth/refresh", new RefreshRequest(refreshToken), ProtocolJson.Default.RefreshRequest, ProtocolJson.Default.AuthResponse, cancellationToken)!;
    public Task LogoutAsync(CancellationToken cancellationToken)
        => Send<object, object>(HttpMethod.Post, "auth/logout", null, null, null, cancellationToken);
    public Task<ServerDto> CreateServerAsync(string name, CancellationToken cancellationToken)
        => Send(HttpMethod.Post, "servers", new CreateServerRequest(name), ProtocolJson.Default.CreateServerRequest, ProtocolJson.Default.ServerDto, cancellationToken)!;
    public async Task<IReadOnlyList<ServerDto>> ListServersAsync(CancellationToken cancellationToken)
        => await Send(HttpMethod.Get, "servers", (object?)null, null, ProtocolJson.Default.ServerDtoArray, cancellationToken) ?? [];
    public async Task<IReadOnlyList<ChannelDto>> ListChannelsAsync(Guid serverId, CancellationToken cancellationToken)
        => await Send(HttpMethod.Get, $"servers/{serverId}/channels", (object?)null, null, ProtocolJson.Default.ChannelDtoArray, cancellationToken) ?? [];
    public Task<ServerDto> JoinAsync(string inviteCode, CancellationToken cancellationToken)
        => Send(HttpMethod.Post, "servers/join", new JoinRequest(inviteCode), ProtocolJson.Default.JoinRequest, ProtocolJson.Default.ServerDto, cancellationToken)!;
    public Task<MessagePageDto> ListMessagesAsync(Guid channelId, Guid? before, int limit, CancellationToken cancellationToken)
        => Send(HttpMethod.Get, $"channels/{channelId}/messages?limit={limit}" + (before is Guid id ? $"&before={id}" : ""),
            (object?)null, null, ProtocolJson.Default.MessagePageDto, cancellationToken)!;
    public Task<MessageDto> SendMessageAsync(Guid channelId, SendMessageRequest request, string idempotencyKey, CancellationToken cancellationToken)
        => Send(HttpMethod.Post, $"channels/{channelId}/messages", request, ProtocolJson.Default.SendMessageRequest, ProtocolJson.Default.MessageDto, cancellationToken, idempotencyKey)!;
    public Task<VoiceJoinDto> JoinVoiceAsync(Guid channelId, bool mute, bool deaf, CancellationToken cancellationToken)
        => Send(HttpMethod.Post, $"channels/{channelId}/voice/join", new VoiceFlags(mute, deaf), ProtocolJson.Default.VoiceFlags, ProtocolJson.Default.VoiceJoinDto, cancellationToken)!;
    public Task LeaveVoiceAsync(CancellationToken cancellationToken)
        => Send<object, object>(HttpMethod.Post, "voice/leave", null, null, null, cancellationToken);
    public Task<VoiceStateDto> PatchVoiceAsync(bool mute, bool deaf, CancellationToken cancellationToken)
        => Send(HttpMethod.Patch, "voice/state", new VoiceFlags(mute, deaf), ProtocolJson.Default.VoiceFlags, ProtocolJson.Default.VoiceStateDto, cancellationToken)!;

    public async Task<AttachmentDto> UploadAsync(PickedImage image, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        using var stream = new StreamContent(image.Content);
        stream.Headers.ContentType = new MediaTypeHeaderValue(image.MimeType);
        content.Add(stream, "file", image.FileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, Combine("attachments")) { Content = content };
        Authorize(request);
        using var response = await _http.SendAsync(request, cancellationToken);
        var json = await Read(response, 64 * 1024, cancellationToken);
        Ensure(response, json);
        return JsonSerializer.Deserialize(json, ProtocolJson.Default.AttachmentDto)
            ?? throw new ChatApiException("invalid_response", "Empty upload response.");
    }

    public async Task<byte[]?> DownloadAsync(Uri url, int maxBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await response.Content.LoadIntoBufferAsync(maxBytes, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private async Task<TOut?> Send<TIn, TOut>(HttpMethod method, string path, TIn? body, JsonTypeInfo<TIn>? input,
        JsonTypeInfo<TOut>? output, CancellationToken cancellationToken, string? idempotency = null)
        where TIn : class
        where TOut : class
    {
        using var request = new HttpRequestMessage(method, Combine(path));
        Authorize(request);
        if (idempotency is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotency);
        if (body is not null && input is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body, input), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await Read(response, 64 * 1024, cancellationToken);
        Ensure(response, json);
        if (output is null || response.StatusCode == HttpStatusCode.NoContent || json.Length == 0) return null;
        return JsonSerializer.Deserialize(json, output) ?? throw new ChatApiException("invalid_response", "Empty API response.");
    }

    private void Authorize(HttpRequestMessage request)
    {
        if (_token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    private Uri Combine(string path) => new(ApiBase.AbsoluteUri.TrimEnd('/') + "/" + path.TrimStart('/'));

    private static async Task<string> Read(HttpResponseMessage response, int max, CancellationToken cancellationToken)
    {
        await response.Content.LoadIntoBufferAsync(max, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static void Ensure(HttpResponseMessage response, string json)
    {
        if (response.IsSuccessStatusCode) return;
        ApiError? error = null;
        try { error = JsonSerializer.Deserialize(json, ProtocolJson.Default.ApiError); } catch (JsonException) { }
        throw new ChatApiException(error?.Code ?? "http_error", error?.Message ?? $"HTTP {(int)response.StatusCode}");
    }

    public void Dispose() => _http.Dispose();
}
