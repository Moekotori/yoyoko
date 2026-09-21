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
{
    NullSystemTransportControls.Instance.Publish(null);
    var user = new UserDto(Guid.NewGuid(), "ada", "Ada");
    var voiceChannel = Guid.NewGuid();
    var voiceServer = Guid.NewGuid();
    var api = System.Reflection.DispatchProxy.Create<Chat.Core.Sessions.IChatApi, VoiceJoinIo>();
    var voiceMedia = System.Reflection.DispatchProxy.Create<IVoiceMedia, VoiceJoinIo>();
    ((VoiceJoinIo)(object)api).Bind(user.Id, voiceServer, voiceChannel);
    var descriptor = new InstanceDescriptor(new(Guid.NewGuid()), new("http://localhost:8080"), "Home");
    var session = new Chat.Core.Sessions.InstanceSession(descriptor, user, api,
        System.Reflection.DispatchProxy.Create<IMessageCache, VoiceJoinIo>(),
        System.Reflection.DispatchProxy.Create<ICredentialVault, VoiceJoinIo>(),
        () => throw new InvalidOperationException("gateway"), voiceMedia, "access", "refresh");
    var controls = new RecordingTransport();
    var preference = new TogglePreference();
    using var binder = new SystemTransportBinder(controls, preference, "yoyoko");
    await session.Voice.JoinAsync(voiceChannel, default);
    binder.Attach(session);
    Check(controls.Current is null, "OS session stays clear while headset keys are off");
    preference.SetHeadsetMediaKeys(true);
    Check(controls.Current is { AppName: "yoyoko", Community: "Home", Muted: false }, "opt-in publishes community while in voice");
    controls.Raise(TransportCommand.Mute);
    Check(session.Voice.SelfMute && controls.Current?.Muted == true, "headset pause mutes the joined session");
    await session.Voice.SetDeafAsync(true, default);
    Check(controls.Current?.Muted == true, "deafen keeps the OS session paused");
    var published = controls.Publishes;
    preference.SetHeadsetMediaKeys(true);
    Check(controls.Publishes == published, "identical now-playing state is not republished");
    await session.Voice.LeaveAsync(default);
    Check(controls.Current is null, "leaving voice clears the OS session");
    await session.DisposeAsync();
}
{
    var reconnectUser = new UserDto(Guid.NewGuid(), "ada", "Ada");
    var reconnectChannel = Guid.NewGuid();
    var reconnectApi = System.Reflection.DispatchProxy.Create<Chat.Core.Sessions.IChatApi, VoiceJoinIo>();
    ((VoiceJoinIo)(object)reconnectApi).Bind(reconnectUser.Id, Guid.NewGuid(), reconnectChannel);
    var reconnectMedia = new ReconnectMedia();
    var reconnectVoice = new VoiceRuntime(reconnectApi, reconnectMedia, reconnectUser.Id);
    await reconnectVoice.JoinAsync(reconnectChannel, default);
    Check(reconnectMedia.Connects == 1 && reconnectVoice.Joined, "voice joins media once");
    reconnectMedia.RaiseSpeaking(reconnectUser.Id.ToString());
    Check(reconnectVoice.IsSpeaking(reconnectUser.Id), "speaking follows LiveKit identities");
    reconnectMedia.RaiseSpeaking();
    Check(!reconnectVoice.IsSpeaking(reconnectUser.Id), "speaking clears when nobody is active");
    reconnectMedia.FailNext = true;
    reconnectMedia.Trip();
    var waited = 0;
    while (waited < 120 && (reconnectVoice.Reconnecting || reconnectVoice.MediaError is not null))
    { await Task.Delay(50); waited++; }
    Check(reconnectMedia.Connects >= 2 && reconnectVoice.Joined && reconnectVoice.MediaError is null, "voice reconnects after media fault");
    await reconnectVoice.DisposeAsync();
}
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
var messageDelete = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/message-delete.json")), ProtocolJson.Default.GatewayEnvelope)!;
Check(messageDelete.Event == "MESSAGE_DELETE" && messageDelete.Data.Deserialize(ProtocolJson.Default.MessageDeleteDto)!.ChannelId == message.ChannelId, "message delete fixture");
var typingStart = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/typing-start.json")), ProtocolJson.Default.GatewayEnvelope)!;
Check(typingStart.Event == "TYPING_START" && typingStart.Seq is null && typingStart.Data.Deserialize(ProtocolJson.Default.TypingDto)!.DisplayName == "Ada", "typing start fixture has no resume seq");
var userUpdate = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/user-update.json")), ProtocolJson.Default.GatewayEnvelope)!;
Check(userUpdate.Event == "USER_UPDATE", "user update fixture");
Check(userUpdate.Data.Deserialize(ProtocolJson.Default.UserDto) is { Avatar.Animated: true, Banner: null }, "animated avatar flag");
var server = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/server.json")), ProtocolJson.Default.ServerDto)!;
Check(server.CooldownSeconds == 5 && server.BlockedWords is ["spam"], "server moderation fixture");
Check(InstanceManager.NormalizeAddress("friends.example.com").Scheme == "https", "discovery defaults to HTTPS");
Check(InstanceManager.NormalizeAddress("localhost:8080").Scheme == "http", "loopback defaults to HTTP");
Check(WorkspaceAddress.Resolve(" localhost ", "http://10.19.144.83:8080") == "http://10.19.144.83:8080/", "localhost shortcut uses configured LAN server and port");
Check(WorkspaceAddress.Resolve("localhost", "http://localhost:8080") == "http://localhost:8080/", "localhost shortcut can target the local instance");
Check(WorkspaceAddress.Resolve("HTTP://LOCALHOST/", "http://192.168.1.10:9090") == "http://192.168.1.10:9090/", "localhost shortcut follows configured default changes");
Check(WorkspaceAddress.Resolve("localhost:8081", "http://10.19.144.83:8080") == "http://localhost:8081/", "explicit loopback port is not redirected");
Check(WorkspaceAddress.Resolve("localhost.example.com", "http://10.19.144.83:8080") == "https://localhost.example.com/", "localhost-like domain is not redirected");
var blankName = RegisterIdentity.From("  ");
Check(blankName.Username == "User" && blankName.DisplayName == "User" && !blankName.ExplicitUsername, "empty join name defaults to User");
var typedName = RegisterIdentity.From(" Alice ");
Check(typedName.Username == "Alice" && typedName.DisplayName == "Alice" && typedName.ExplicitUsername, "join name uses typed username");
var displayOnly = RegisterIdentity.From("小明");
Check(displayOnly.Username == "User" && displayOnly.DisplayName == "小明" && !displayOnly.ExplicitUsername, "non-ascii join name stays as display");
Check(RegisterIdentity.From(null).UsernameAt(1) == "User2", "default username collision suffix");
try { WorkspaceAddress.Resolve("localhost", "localhost"); throw new Exception("Recursive alias accepted"); }
catch (ClientFault) { Check(true, "recursive default shortcut is rejected"); }
var join = WorkspaceInvite.Parse("http://10.19.144.83:8080/join/ABCD2345", "http://127.0.0.1:8080");
Check(join.Address == "http://10.19.144.83:8080/" && join.CommunityCode == "ABCD2345", "join URL carries origin and community code");
Check(WorkspaceInvite.Link(new Uri("http://10.19.144.83:8080/"), "abcd2345") == "http://10.19.144.83:8080/join/ABCD2345", "copied invite is origin plus join code");
Check(WorkspaceInvite.Parse("localhost/join/ABCD2345", "http://10.19.144.83:8080").Address == "http://10.19.144.83:8080/", "localhost join shortcut uses configured server");
Check(WorkspaceInvite.Parse("ABCD2345", "http://10.19.144.83:8080").CommunityCode == "ABCD2345", "bare invite code uses configured server");
Check(WorkspaceInvite.CodeFrom("http://10.19.144.83:8080/join/abcd2345") == "ABCD2345", "join extracts community code from a share URL");
Check(WorkspaceInvite.VoiceLink(new Uri("http://10.19.144.83:8080/"), Guid.Parse("01950000-0000-7000-8000-000000000040"))
    == "http://10.19.144.83:8080/voice/01950000-0000-7000-8000-000000000040", "web voice link is origin plus channel");
Check(WorkspaceInvite.Parse("http://10.19.144.83:8080/voice/01950000-0000-7000-8000-000000000040", "http://127.0.0.1:8080").Address
    == "http://10.19.144.83:8080/", "voice page URL still discovers the instance");
var voiceRoom = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/voice-room.json")), ProtocolJson.Default.VoiceRoomDto)!;
Check(voiceRoom.Kind == "voice" && voiceRoom.ParticipantCount == 2, "voice room preview fixture");
var voiceGuest = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/voice-guest-join.json")), ProtocolJson.Default.VoiceGuestSessionDto)!;
Check(voiceGuest.Room.ChannelId == voiceRoom.ChannelId && voiceGuest.Join.State.DisplayName == "Ada", "voice guest join fixture");
var local = WorkspaceInvite.Parse("localhost", "http://10.19.144.83:8080");
Check(local.Address == "http://10.19.144.83:8080/" && local.CommunityCode is null, "localhost remains an address shortcut");
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
var packDir = Path.Combine(Path.GetTempPath(), "chat-lang-" + Guid.NewGuid());
try
{
    var packs = LanguagePackStore.Open(packDir);
    var overlay = """{"code":"en","name":"English","strings":{"Settings":"Prefs"}}"""u8.ToArray();
    packs.ImportBytes(overlay);
    var overlayed = new TextCatalog(packs);
    Check(overlayed.Get(Locale.English, TextKey.Settings) == "Prefs", "imported pack overlays built-in English");
    Check(overlayed.Get(Locale.English, TextKey.Language) == "Language", "overlay keeps missing keys");
    var korean = """{"code":"ko","name":"한국어","fallback":"en","strings":{"Settings":"설정"}}"""u8.ToArray();
    var ko = packs.ImportBytes(korean);
    Check(ko.Code == "ko" && overlayed.Available.Any(item => item.Code == "ko"), "custom locale appears in available list");
    Check(overlayed.Get(ko, TextKey.Settings) == "설정", "custom locale uses pack strings");
    Check(overlayed.Get(ko, TextKey.Language) == "Language", "custom locale falls back to English");
    Check(Locale.Parse("ko-KR", packs.Available).Code == "ko", "system ko-KR maps to imported pack");
    var template = packs.TemplateJson(new TextCatalog());
    Check(template.Contains("\"Settings\"", StringComparison.Ordinal) && template.Contains("\"code\": \"xx\"", StringComparison.Ordinal), "template json includes keys");
    try { packs.ImportBytes("{}"u8.ToArray()); throw new Exception("empty pack accepted"); }
    catch (LanguagePackException fault) { Check(fault.Key == TextKey.LanguagePackInvalid, "invalid pack rejected"); }
    packs.Remove("en");
    Check(overlayed.Get(Locale.English, TextKey.Settings) == "Settings", "removing overlay restores built-in");
}
finally { if (Directory.Exists(packDir)) Directory.Delete(packDir, true); }
Check(catalog.Get(Locale.Chinese, TextKey.ConnectedServer) == "已连接", "Chinese connected label");
Check(catalog.Get(Locale.Chinese, TextKey.DisconnectServer) == "断开", "Chinese disconnect label");
Check(MessageMarkup.MentionsUser("hey @Ada now", "ada"), "mention scan is case-insensitive");
Check(!MessageMarkup.MentionsUser("mail ada@example.com", "ada"), "email is not a mention");
Check(MessageMarkup.MentionsEveryone("ping @everyone now"), "everyone mention is detected");
Check(!MessageMarkup.MentionsEveryone("mail everyone@example.com"), "email is not everyone");
Check(MessageMarkup.MentionsHere("ping @here now"), "here mention is detected");
Check(!MessageMarkup.MentionsHere("mail here@example.com"), "email is not here");
Check(MessageMarkup.MentionsAccount("hi @everyone", "ada"), "everyone counts as a mention");
Check(MessageMarkup.MentionsAccount("hi", "ada", mentionEveryone: true), "mention_everyone flag is honored");
Check(MessageMarkup.MentionsAccount("hi", "ada", mentionHere: true), "mention_here flag is honored");
Check(MessageMarkup.MentionsAccount("hi @林", "lin", displayName: "林"), "display-name mention is detected");
Check(MessageMarkup.Parse("hi @林").Any(span => span.Kind == MarkupKind.Mention && span.Text == "林"), "unicode mention parses");
Check(MessageMarkup.TryComposerQuery("hi @ad", 6, out var mentionQuery) && mentionQuery.Filter == "ad", "composer mention query");
Check(MessageMarkup.TryComposerQuery("hi @林", 5, out var cjkQuery) && cjkQuery.Filter == "林", "composer accepts unicode mention");
Check(!MessageMarkup.TryComposerQuery("mail ada@x.com", 10, out _), "email is not a composer mention");
Check(MessageMarkup.Parse("**bold** and `x`").Any(span => span.Kind == MarkupKind.Bold && span.Text == "bold"), "parses bold markup");
Check(MessageMarkup.IdAfter(Guid.Parse("01950000-0000-7000-8000-000000000011"), Guid.Parse("01950000-0000-7000-8000-000000000010")), "UUIDv7 string order is chronological");
{
    var general = Guid.Parse("01950000-0000-7000-8000-0000000000a1");
    var voiceJump = Guid.Parse("01950000-0000-7000-8000-0000000000a2");
    var extra = Guid.Parse("01950000-0000-7000-8000-0000000000a3");
    var channels = new[] { (general, "general"), (voiceJump, "voice"), (extra, "offtopic") };
    var visited = new Dictionary<Guid, long> { [extra] = 20, [general] = 10 };
    var recents = JumpRank.Channels(channels, item => item.Item1, item => item.Item2, visited, "");
    Check(recents[0].Item1 == extra && recents[1].Item1 == general && recents.Count == 2, "empty jump lists recents first");
    var filtered = JumpRank.Channels(channels, item => item.Item1, item => item.Item2, visited, "off");
    Check(filtered.Count == 1 && filtered[0].Item1 == extra, "query still prefers a recent hit");
    Check(JumpRank.Channels(channels, item => item.Item1, item => item.Item2, new Dictionary<Guid, long>(), "").Count == 3, "no recents lists every channel");
}
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
    await cache.NoteArrivalAsync(scopeA, newer, mentioned: true, default);
    await cache.SaveDraftAsync(scopeA, message.ChannelId, "later", default);
    await cache.SaveNotifyAsync(scopeA, message.ChannelId, ChannelNotify.Mentions, default);
    var inbox = (await cache.LoadInboxAsync(scopeA, default)).Single(item => item.ChannelId == message.ChannelId);
    Check(inbox.LastMessageId == newer.Id && inbox.Draft == "later" && inbox.Notify == ChannelNotify.Mentions, "inbox stores latest, draft and notify");
    Check(inbox.HasMention && inbox.HasUnread(Guid.CreateVersion7()), "unacked mention and unread");
    Check(!inbox.HasUnread(newer.AuthorId), "own latest message is not unread");
    await cache.SaveReadAsync(scopeA, message.ChannelId, newer.Id, default);
    inbox = (await cache.LoadInboxAsync(scopeA, default)).Single(item => item.ChannelId == message.ChannelId);
    Check(!inbox.HasUnread(Guid.CreateVersion7()) && !inbox.HasMention, "ack clears unread and mention");
    await cache.SaveVisitAsync(scopeA, message.ChannelId, 42, default);
    inbox = (await cache.LoadInboxAsync(scopeA, default)).Single(item => item.ChannelId == message.ChannelId);
    Check(inbox.VisitedAt == 42, "visit timestamp persists");
    Check((await cache.LoadInboxAsync(scopeB, default)).All(item => item.ChannelId != message.ChannelId || item.Draft is null), "inbox is instance-scoped");
    await cache.CommitMessageAsync(scopeA, newer, "sess-1", 9, default);
    Check((await cache.LoadCursorAsync(scopeA, default)).Seq == 9 && (await cache.LoadCursorAsync(scopeA, default)).SessionId == "sess-1", "event commit writes cursor");
    await cache.CommitMessageAsync(scopeA, newer, "sess-1", 3, default);
    Check((await cache.LoadCursorAsync(scopeA, default)).Seq == 9, "cursor does not rewind");
    var pending = new OutboxRecord(Guid.CreateVersion7(), message.ChannelId, "idem-outbox", "queued later", null,
        DateTimeOffset.UtcNow, "sending", [], []);
    await cache.SaveOutboxAsync(scopeA, pending, default);
    Check((await cache.LoadOutboxAsync(scopeA, default)).Any(item => item.LocalId == pending.LocalId), "outbox persists pending send");
    await cache.RemoveOutboxAsync(scopeA, pending.LocalId, default);
    Check((await cache.LoadOutboxAsync(scopeA, default)).All(item => item.LocalId != pending.LocalId), "outbox removes after ack");
    var echo = System.Reflection.DispatchProxy.Create<Chat.Core.Sessions.IChatApi, EchoSendIo>();
    var outbound = new OutboundQueue(scopeA, scopeA.AccountId, echo, cache);
    var live = new ChannelTimeline(scopeA, message.ChannelId, scopeA.AccountId, echo, cache, outbound);
    await live.SendAsync("optimistic", [], null, default);
    Check(live.Items.Any(item => item.Message.Content == "optimistic" && item.LocalId != Guid.Empty), "optimistic row appears before flush");
    await outbound.FlushAsync(default);
    var acked = live.Items.Single(item => item.Message.Content == "optimistic");
    Check(acked.Status == SendStatus.Sent && acked.Message.Id != acked.LocalId, "ack swaps in the server id");
    Check((await cache.LoadOutboxAsync(scopeA, default)).Count == 0, "successful send clears outbox");
    await outbound.DisposeAsync();
    await cache.RemoveMessageAsync(scopeA, message.ChannelId, newer.Id, default);
    Check((await cache.ReadPageAsync(scopeA, message.ChannelId, null, 50, default)).Items.All(item => item.Id != newer.Id), "deleted message leaves cache");
    await cache.UpsertAsync(scopeA, newer, default);
    await cache.ClearChannelAsync(scopeA, message.ChannelId, default);
    Check((await cache.ReadPageAsync(scopeA, message.ChannelId, null, 50, default)).Items.Count == 0, "channel cache clear");
    Check((await cache.LoadInboxAsync(scopeA, default)).All(item => item.ChannelId != message.ChannelId), "channel clear drops inbox");
    await cache.UpsertAsync(scopeA, message, default);
    await cache.UpsertAsync(scopeA, newer, default);
    var forbidden = System.Reflection.DispatchProxy.Create<Chat.Core.Sessions.IChatApi, ForbiddenListIo>();
    var timeline = new ChannelTimeline(scopeA, message.ChannelId, scopeA.AccountId, forbidden, cache);
    await timeline.LoadLatestAsync(default);
    Check(timeline.AccessDenied && timeline.Items.Count == 0, "forbidden list clears timeline");
    Check((await cache.ReadPageAsync(scopeA, message.ChannelId, null, 50, default)).Items.Count == 0, "forbidden list purges cache");
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
var markup = MessageMarkup.Parse("say **bold** and `code`");
Check(markup.Any(span => span is { Kind: MarkupKind.Bold, Text: "bold" })
    && markup.Any(span => span is { Kind: MarkupKind.Code, Text: "code" }), "markdown bold and code");
Check(MessageMarkup.Parse("~~old~~")[0] is { Kind: MarkupKind.Strike, Text: "old" }, "strikethrough");
var mathSpans = MessageMarkup.Parse("energy $E=mc^2$ rest");
Check(mathSpans.Count == 3 && mathSpans[1] is { Kind: MarkupKind.Math, Text: "E=mc^2" }, "inline latex delimiter");
Check(MessageMarkup.Parse("$$\\frac{1}{2}$$")[0].Kind == MarkupKind.DisplayMath, "display latex delimiter");
Check(MessageMarkup.MentionsUser("ping @alice now", "alice"), "mention scan ignores surrounding text");
Check(MessageMarkup.Parse("[docs](https://example.com)")[0] is { Kind: MarkupKind.Link, Text: "docs", Extra: "https://example.com" }, "markdown link");
Check(MathMarkup.Parse("\\frac{1}{2}") is MathFrac, "latex fraction tree");
Check(MathMarkup.Parse("\\alpha") is MathText { Text: "α" }, "latex greek");
Check(MathMarkup.Parse("x^2") is MathScripts, "latex superscript");
Check(MathMarkup.Parse("\\mathbb{R}") is MathText { Text: "ℝ" }, "latex blackboard");
Check(MessageMarkup.IsPlain("hello there"), "plain chat skips markup");
Check(!MessageMarkup.IsPlain("**x**"), "emphasis is not plain");
Check(ReferenceEquals(MessageMarkup.Parse("**bold**"), MessageMarkup.Parse("**bold**")), "markup parse is cached");
Check(MessageMarkup.Parse("# Title")[0] is { Kind: MarkupKind.Heading, Text: "Title", Extra: "1" }, "heading");
Check(MessageMarkup.Parse("- one\n- two").Count(span => span.Kind == MarkupKind.ListItem) == 2, "list items");
Check(MessageMarkup.Parse("> quoted")[0] is { Kind: MarkupKind.Quote, Text: "quoted" }, "blockquote");
Check(MessageMarkup.Parse("| a | b |\n| --- | --- |\n| 1 | 2 |").Any(span => span.Kind == MarkupKind.Table && span.Text.Contains('1')), "table row");
Check(MathMarkup.TryFlatten(MathMarkup.Parse("x^2"), out var flat) && flat.Contains('²'), "simple latex flattens");
Check(!MathMarkup.TryFlatten(MathMarkup.Parse("\\frac{1}{2}"), out _), "fraction stays laid out");
Check(MathMarkup.Parse("\\begin{pmatrix}1&2\\\\3&4\\end{pmatrix}") is MathMatrix matrix && matrix.Rows.Length == 2, "pmatrix");
Check(MessageMarkup.Parse("<b>bold</b>")[0] is { Kind: MarkupKind.Bold, Text: "bold" }, "html bold");
Check(MessageMarkup.Parse("say <em>x</em> and <u>y</u>").Any(span => span is { Kind: MarkupKind.Italic, Text: "x" })
    && MessageMarkup.Parse("say <em>x</em> and <u>y</u>").Any(span => span is { Kind: MarkupKind.Underline, Text: "y" }), "html italic and underline");
Check(MessageMarkup.Parse("<a href=\"https://example.com\">docs</a>")[0] is { Kind: MarkupKind.Link, Text: "docs", Extra: "https://example.com" }, "html link");
Check(MessageMarkup.Parse("a<br>b").Any(span => span.Text.Contains('\n')), "html line break");
Check(MessageMarkup.Parse("<h2>Title</h2>")[0] is { Kind: MarkupKind.Heading, Text: "Title", Extra: "2" }, "html heading");
Check(MessageMarkup.Parse("<ul><li>one</li><li>two</li></ul>").Count(span => span.Kind == MarkupKind.ListItem) == 2, "html list");
Check(MessageMarkup.Parse("A &amp; B")[0].Text.Contains('&') && !MessageMarkup.Parse("A &amp; B")[0].Text.Contains("&amp;"), "html entity");
Check(MessageMarkup.Parse("<script>alert(1)</script>ok")[0].Text == "ok", "html script is dropped");
Check(MessageMarkup.Parse("<a href=\"javascript:alert(1)\">x</a>")[0] is { Kind: MarkupKind.Text, Text: "x" }, "javascript href is not a link");
Check(MessageMarkup.IsPlain("3 < 5 and 4"), "comparison is still plain");
Check(!MessageMarkup.IsPlain("<b>x</b>"), "html is not plain");
Console.WriteLine($"{checks} foundation checks passed.");

public class EchoSendIo : System.Reflection.DispatchProxy
{
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method?.Name == "SendMessageAsync")
        {
            var channel = (Guid)args![0]!;
            var request = (SendMessageRequest)args[1]!;
            return Task.FromResult(new MessageDto(Guid.CreateVersion7(), channel, Guid.Empty, "text", request.Content,
                DateTimeOffset.UtcNow, null, request.ReplyTo, [], [], [], [], null));
        }
        if (method?.Name == "ListMessagesAsync")
            return Task.FromResult(new MessagePageDto([], null));
        if (method?.ReturnType == typeof(Task)) return Task.CompletedTask;
        throw new InvalidOperationException("Unexpected: " + method?.Name);
    }
}

public class ForbiddenListIo : System.Reflection.DispatchProxy
{
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method?.Name == "ListMessagesAsync")
            throw new Chat.Core.Sessions.ChatApiException("forbidden", "Missing permission.");
        if (method?.ReturnType == typeof(Task)) return Task.CompletedTask;
        throw new InvalidOperationException("Unexpected: " + method?.Name);
    }
}

public class NoVoiceIo : System.Reflection.DispatchProxy
{
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method?.Name is "add_Faulted" or "remove_Faulted" or "add_SpeakingChanged" or "remove_SpeakingChanged")
            return null;
        throw new InvalidOperationException("Unexpected voice IO before joining: " + method?.Name);
    }
}

public class VoiceJoinIo : System.Reflection.DispatchProxy
{
    public Guid UserId, ServerId, ChannelId;
    public bool Mute;
    public void Bind(Guid user, Guid server, Guid channel)
    {
        UserId = user;
        ServerId = server;
        ChannelId = channel;
    }
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        switch (method?.Name)
        {
            case "JoinVoiceAsync":
                return Task.FromResult(new VoiceJoinDto(
                    new("t", new Uri("wss://localhost/rtc"), "room", DateTimeOffset.UtcNow.AddMinutes(1)),
                    new(UserId, ServerId, ChannelId, Mute, false, "Ada", AudioQualities.Studio),
                    new(AudioQualities.Studio, 48000, 2, 510_000, 20, true, true), AudioQualities.Studio));
            case "PatchVoiceAsync":
                Mute = (bool)args![0]!;
                return Task.FromResult(new VoiceStateDto(UserId, ServerId, ChannelId, Mute, (bool)args[1]!, "Ada", AudioQualities.Studio));
            case "LeaveVoiceAsync":
            case "ConnectAsync":
            case "LeaveAsync":
            case "SetMutedAsync":
            case "SetDeafenedAsync":
                return Task.CompletedTask;
            case "get_Available":
                return true;
            case "Dispose":
            case "add_Faulted":
            case "remove_Faulted":
            case "add_SpeakingChanged":
            case "remove_SpeakingChanged":
            case "SetAccessToken":
                return null;
            case "LoadOutboxAsync":
                return Task.FromResult<IReadOnlyList<OutboxRecord>>([]);
            case "CountOutboxAsync":
                return Task.FromResult(0);
            case "LoadCursorAsync":
                return Task.FromResult<(string?, long)>((null, 0));
            default:
                if (method?.ReturnType == typeof(Task)) return Task.CompletedTask;
                if (method?.ReturnType == typeof(ValueTask)) return ValueTask.CompletedTask;
                throw new InvalidOperationException("Unexpected: " + method?.Name);
        }
    }
}

sealed class ReconnectMedia : IVoiceMedia
{
    public int Connects;
    public bool FailNext;
    public bool Available => true;
    public event Action<string>? Faulted;
    public event Action<IReadOnlyList<string>>? SpeakingChanged;
    public Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken)
    {
        Connects++;
        if (FailNext)
        {
            FailNext = false;
            return Task.FromException(new InvalidOperationException("down"));
        }
        return Task.CompletedTask;
    }
    public Task LeaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetQualityAsync(AudioCaptureOptions audio, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<AudioDeviceList> ListDevicesAsync(CancellationToken cancellationToken) => Task.FromResult(new AudioDeviceList([], []));
    public Task SetDevicesAsync(AudioRoute route, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartLoopbackAsync(AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopLoopbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Trip() => Faulted?.Invoke("Media worker exited.");
    public void RaiseSpeaking(params string[] ids) => SpeakingChanged?.Invoke(ids);
}

sealed class RecordingTransport : ISystemTransportControls
{
    public TransportNowPlaying? Current { get; private set; }
    public int Publishes { get; private set; }
    public bool Available => true;
    public event Action<TransportCommand>? CommandRequested;
    public event Action? RaiseRequested { add { } remove { } }
    public void BindWindow(nint hwnd) { }
    public void Publish(TransportNowPlaying? session)
    {
        Publishes++;
        Current = session;
    }
    public void Dispose() { }
    public void Raise(TransportCommand command) => CommandRequested?.Invoke(command);
}

sealed class TogglePreference : ISystemTransportPreference
{
    public bool HeadsetMediaKeys { get; private set; }
    public event Action? Changed;
    public void SetHeadsetMediaKeys(bool value)
    {
        HeadsetMediaKeys = value;
        Changed?.Invoke();
    }
}
