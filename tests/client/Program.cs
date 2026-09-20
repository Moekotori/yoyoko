using System.Text.Json;
using Chat.Core.Instances;
using Chat.Core.Messaging;
using Chat.Core.Realtime;
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
var discovery = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/discovery.json")), ProtocolJson.Default.InstanceDiscovery)!;
var envelope = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/message-create.json")), ProtocolJson.Default.GatewayEnvelope)!;
var message = envelope.Data.Deserialize(ProtocolJson.Default.MessageDto)!;
Check(envelope.Seq == 9007199254740993, "64-bit sequence survives wire decoding");
Check(JsonSerializer.Serialize(envelope, ProtocolJson.Default.GatewayEnvelope).Contains("\"seq\":\"9007199254740993\""), "sequence encodes as decimal string");
Check(discovery.ProtocolVersion == 1 && message.Kind == "text", "shared protocol fixtures");
Check(InstanceManager.NormalizeAddress("friends.example.com").Scheme == "https", "discovery defaults to HTTPS");
Check(InstanceManager.NormalizeAddress("localhost:8080").Scheme == "http", "loopback defaults to HTTP");
Check(InstanceManager.NormalizeAddress("192.168.1.10:8080").Host == "192.168.1.10", "LAN IP defaults to HTTP");
try { InstanceManager.NormalizeAddress("http://example.com"); throw new Exception("Insecure URL accepted"); }
catch (ArgumentException) { Check(true, "remote cleartext URL rejected"); }
try { InstanceManager.NormalizeAddress("http://8.8.8.8"); throw new Exception("Public cleartext IP accepted"); }
catch (ArgumentException) { Check(true, "public cleartext IP rejected"); }
Check(!PermissionResolver.Resolve(Permission.SendMessage, [], default, [], new(0, Permission.SendMessage)).HasFlag(Permission.SendMessage), "member permission deny wins");
Check(PermissionResolver.Resolve(Permission.Administrator, [], default, [], new(0, Permission.SendMessage)).HasFlag(Permission.SendMessage), "administrator override");
Check(ReconnectPolicy.Delay(99, 1) <= TimeSpan.FromSeconds(37.5), "backoff remains bounded");
await using var media = new UnavailableMediaService();
Check(media.Capabilities == MediaCapabilities.None, "no false media capabilities");
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
