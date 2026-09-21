using System.Net;
using System.Text.Json;
using Chat.Core.Instances;
using Chat.Core;
using Chat.Localization;
using Chat.Core.Messaging;
using Chat.Core.Realtime;
using Chat.Core.Voice;
using Chat.Domain.Instances;
using Chat.Domain.Permissions;
using Chat.Media;
using Chat.Protocol;
using Chat.Storage;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
var offlineVoice = new VoiceRuntime(
    System.Reflection.DispatchProxy.Create<Chat.Core.Sessions.IChatApi, NoVoiceIo>(),
    System.Reflection.DispatchProxy.Create<IVoiceMedia, NoVoiceIo>(), Guid.NewGuid());
await offlineVoice.SetMuteAsync(true, default);
Check(offlineVoice.SelfMute && !offlineVoice.Joined, "mute can be set before joining without API or media IO");
await offlineVoice.SetMuteAsync(false, default);
await offlineVoice.SetDeafAsync(true, default);
Check(offlineVoice.SelfMute && offlineVoice.SelfDeaf, "deafen also mutes before joining");
await offlineVoice.SetDeafAsync(false, default);
Check(!offlineVoice.SelfMute && !offlineVoice.SelfDeaf, "undeafen restores previously open microphone");
await offlineVoice.SetMuteAsync(true, default);
await offlineVoice.SetDeafAsync(true, default);
await offlineVoice.SetDeafAsync(false, default);
Check(offlineVoice.SelfMute && !offlineVoice.SelfDeaf, "undeafen preserves manual mute");
await offlineVoice.SetDeafAsync(true, default);
await offlineVoice.SetMuteAsync(false, default);
Check(!offlineVoice.SelfMute && !offlineVoice.SelfDeaf, "unmute also clears deafen before joining");
var discovery = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/discovery.json")), ProtocolJson.Default.InstanceDiscovery)!;
var envelope = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/message-create.json")), ProtocolJson.Default.GatewayEnvelope)!;
var message = envelope.Data.Deserialize(ProtocolJson.Default.MessageDto)!;
Check(envelope.Seq == 9007199254740993, "64-bit sequence survives wire decoding");
Check(JsonSerializer.Serialize(envelope, ProtocolJson.Default.GatewayEnvelope).Contains("\"seq\":\"9007199254740993\""), "sequence encodes as decimal string");
Check(discovery.ProtocolVersion == 1 && message.Kind == "text", "shared protocol fixtures");
Check(discovery.MaxAttachmentBytes == ProtocolVersion.MaxAttachmentBytes && discovery.MaxAttachmentsPerMessage == ProtocolVersion.MaxAttachmentsPerMessage, "discovery advertises attachment limits");
var channelPatch = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/channel-patch.json")), ProtocolJson.Default.PatchChannelRequest)!;
Check(channelPatch.Name == "讨论" && channelPatch.AudioQuality is null, "optional channel rename fixture");
var channelDelete = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/channel-delete.json")), ProtocolJson.Default.GatewayEnvelope)!;
Check(channelDelete.Event == "CHANNEL_DELETE" && channelDelete.Data.Deserialize(ProtocolJson.Default.ChannelDto)!.Name == "讨论", "channel deletion event fixture");
var voice = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/voice-state.json")), ProtocolJson.Default.GatewayEnvelope)!;
Check(voice.Event == "VOICE_STATE_UPDATE", "voice state fixture");
var voiceState = voice.Data.Deserialize(ProtocolJson.Default.VoiceStateDto)!;
Check(voiceState.DisplayName == "Ada", "voice state display name");
Check(voiceState.AudioQuality == AudioQualities.Studio, "voice state fixture uses studio");
Check(AudioQualities.Profile(AudioQualities.VeryHigh).BitrateBps == 384_000, "very high is 384 kbps stereo");
Check(AudioQualities.Profile(AudioQualities.Studio).BitrateBps == 510_000, "studio is Opus maximum");
Check(AudioQualities.FrameSamples(AudioQualities.Standard) == 960, "standard frame is 20 ms mono");
Check(AudioQualities.FrameSamples(AudioQualities.Studio) == 1920, "studio frame is 20 ms stereo");
Check(AudioQualities.Clamp(AudioQualities.Studio, AudioQualities.High) == AudioQualities.High, "send quality clamps to channel max");
var userUpdate = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/user-update.json")), ProtocolJson.Default.GatewayEnvelope)!;
Check(userUpdate.Event == "USER_UPDATE", "user update fixture");
Check(userUpdate.Data.Deserialize(ProtocolJson.Default.UserDto)!.Avatar!.Animated, "animated avatar flag");
var server = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/server.json")), ProtocolJson.Default.ServerDto)!;
Check(server.CooldownSeconds == 5 && server.BlockedWords is ["spam"], "server moderation fixture");
Check(InstanceManager.NormalizeAddress("friends.example.com").Scheme == "https", "discovery defaults to HTTPS");
Check(InstanceManager.NormalizeAddress("localhost:8080").Scheme == "http", "loopback defaults to HTTP");
Check(WorkspaceAddress.Resolve(" localhost ", "http://10.19.144.83:8080") == "http://10.19.144.83:8080/", "localhost shortcut uses configured LAN server and port");
Check(WorkspaceAddress.Resolve("HTTP://LOCALHOST/", "http://192.168.1.10:9090") == "http://192.168.1.10:9090/", "localhost shortcut follows configured default changes");
Check(WorkspaceAddress.Resolve("localhost:8081", "http://10.19.144.83:8080") == "http://localhost:8081/", "explicit loopback port is not redirected");
Check(WorkspaceAddress.Resolve("localhost.example.com", "http://10.19.144.83:8080") == "https://localhost.example.com/", "localhost-like domain is not redirected");
try { WorkspaceAddress.Resolve("localhost", "localhost"); throw new Exception("Recursive alias accepted"); }
catch (ClientFault) { Check(true, "recursive default shortcut is rejected"); }
Check(InstanceManager.NormalizeAddress("192.168.1.10:8080").Host == "192.168.1.10", "LAN IP defaults to HTTP");
try { InstanceManager.NormalizeAddress("http://example.com"); throw new Exception("Insecure URL accepted"); }
catch (ClientFault fault) { Check(fault.Key == TextKey.InvalidInstanceAddress, "remote cleartext URL rejected"); }
try { InstanceManager.NormalizeAddress("http://8.8.8.8"); throw new Exception("Public cleartext IP accepted"); }
catch (ClientFault fault) { Check(fault.Key == TextKey.InvalidInstanceAddress, "public cleartext IP rejected"); }
Check(!PermissionResolver.Resolve(Permission.SendMessage, [], default, [], new(0, Permission.SendMessage)).HasFlag(Permission.SendMessage), "member permission deny wins");
Check(PermissionResolver.Resolve(Permission.Administrator, [], default, [], new(0, Permission.SendMessage)).HasFlag(Permission.SendMessage), "administrator override");
Check(ReconnectPolicy.Delay(99, 1) <= TimeSpan.FromSeconds(37.5), "backoff remains bounded");
var catalog = new TextCatalog();
Check(catalog.SameKeys(), "zh/en/ja catalogs share keys");
var keyConsts = typeof(TextKey).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
    .Where(field => field.IsLiteral)
    .Select(field => (string)field.GetRawConstantValue()!)
    .ToHashSet(StringComparer.Ordinal);
var missingKeys = catalog.Keys.Except(keyConsts).OrderBy(key => key).ToArray();
var extraKeys = keyConsts.Except(catalog.Keys).OrderBy(key => key).ToArray();
Check(missingKeys.Length == 0 && extraKeys.Length == 0,
    "TextKey matches catalog keys" + (missingKeys.Length == 0 && extraKeys.Length == 0 ? "" :
        " missing=" + string.Join(",", missingKeys) + " extra=" + string.Join(",", extraKeys)));
Check(Locale.Parse("zh-CN").Code == "zh-Hans", "zh-CN maps to simplified Chinese");
Check(Locale.Parse("ja-JP").Code == "ja", "ja-JP maps to Japanese");
Check(Locale.Parse("fr").Code == "en", "unsupported locale falls back to English");
Check(catalog.Get(Locale.Chinese, TextKey.Settings) == "设置", "Chinese settings label");
Check(catalog.Get(Locale.English, TextKey.Settings) == "Settings", "English settings label");
Check(catalog.Get(Locale.Japanese, TextKey.Settings) == "設定", "Japanese settings label");
Check(catalog.Get(Locale.Chinese, TextKey.ConnectedServer) == "已连接", "Chinese connected label");
Check(catalog.Get(Locale.Chinese, TextKey.DisconnectServer) == "断开", "Chinese disconnect label");
Check(ProtocolVersion.HealthLivePath == "/health/live", "live health path");
{
    var tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    tcp.Start();
    var port = ((System.Net.IPEndPoint)tcp.LocalEndpoint).Port;
    tcp.Stop();
    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();
    var serve = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var body = """{"status":"ok","phase":1}"""u8.ToArray();
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    });
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var rtt = await new Chat.Networking.Http.HttpInstanceDiscovery(http)
        .ProbeAsync(new Uri($"http://127.0.0.1:{port}/"), default);
    await serve;
    listener.Stop();
    Check(rtt >= TimeSpan.Zero && rtt < TimeSpan.FromSeconds(3), "health probe measures live RTT");
}
await using var media = new UnavailableMediaService();
Check(media.Capabilities == MediaCapabilities.None, "no false media capabilities");
var devices = await media.ListDevicesAsync(default);
Check(devices.Inputs.Count == 0 && devices.Outputs.Count == 0, "unavailable media exposes no devices");
await media.SetDevicesAsync(new AudioRoute("in:mic", "out:speakers"), default);
Check(AudioRoute.System.InputDeviceId is null && AudioRoute.System.OutputDeviceId is null, "system route is unset");
var directory = Path.Combine(Path.GetTempPath(), "chat-checks-" + Guid.NewGuid());
try
{
    var cache = new SqliteCache(Path.Combine(directory, "cache.db"));
    await cache.InitializeAsync(default);
    await cache.InitializeAsync(default);
    var first = new InstanceDescriptor(new(discovery.InstanceId), new("http://localhost:8080"), "A");
    var second = new InstanceDescriptor(new(Guid.CreateVersion7()), new("http://localhost:8081"), "B");
    await cache.SaveAsync(first, default);
    await cache.SaveAsync(second, default);
    var scopeA = new CacheScope(first.Id, Guid.CreateVersion7());
    var scopeB = new CacheScope(second.Id, scopeA.AccountId);
    var accountB = new CacheScope(first.Id, Guid.CreateVersion7());
    await cache.UpsertAsync(scopeA, message, default);
    await cache.UpsertAsync(scopeB, message with { Content = "other instance" }, default);
    var newer = message with { Id = Guid.Parse("01950000-0000-7000-8000-000000000011"), Content = "newer" };
    await cache.UpsertAsync(scopeA, newer, default);
    var page = await cache.ReadPageAsync(scopeA, message.ChannelId, null, 1, default);
    Check(page.Items.Single().Content == "newer" && page.Before == newer.Id, "keyset page has next cursor");
    var older = await cache.ReadPageAsync(scopeA, message.ChannelId, page.Before, 1, default);
    Check(older.Items.Single().Content == "Hello, world" && older.Before is null, "next page does not duplicate boundary");
    Check((await cache.ReadPageAsync(scopeB, message.ChannelId, null, 50, default)).Items.Single().Content == "other instance", "instance cache isolation");
    Check((await cache.ReadPageAsync(accountB, message.ChannelId, null, 50, default)).Items.Count == 0, "account cache isolation");
    await cache.SaveCommunityAsync(scopeA, new([], [], []), default);
    Check((await cache.ReadPageAsync(scopeA, message.ChannelId, null, 50, default)).Items.Count == 0, "removed channel messages are purged with community snapshot");
    Check((await cache.ReadPageAsync(scopeB, message.ChannelId, null, 50, default)).Items.Count == 1, "channel removal preserves other instance cache");
    await cache.PurgeAsync(scopeA, default);
    Check((await cache.ReadPageAsync(scopeB, message.ChannelId, null, 50, default)).Items.Count == 1, "logout purge preserves other instance");
    var reopened = new SqliteCache(Path.Combine(directory, "cache.db"));
    Check((await reopened.LoadAsync(default)).Count == 2, "instances survive offline restart");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
if (args.Length == 2 && args[0] == "--gateway")
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await using var gateway = new Chat.Networking.WebSocket.WebSocketConnection();
    await gateway.ConnectAsync(new Uri(args[1]), timeout.Token);
    await using var events = gateway.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
    Check(await events.MoveNextAsync() && events.Current.Op == "hello", "live Gateway hello");
    var identify = JsonSerializer.SerializeToElement(new GatewayIdentify(999, "not-a-token"), ProtocolJson.Default.GatewayIdentify);
    await gateway.SendAsync(new GatewayEnvelope("identify", null, null, identify), timeout.Token);
    Check(!await events.MoveNextAsync(), "live Gateway rejects incompatible version");
}
Console.WriteLine($"{checks} foundation checks passed.");

public class NoVoiceIo : System.Reflection.DispatchProxy
{
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        => throw new InvalidOperationException("Unexpected voice IO before joining: " + method?.Name);
}
