using System.Text.Json;
using Chat.Protocol;

namespace Chat.Core.Realtime;

public sealed class GatewaySession : IAsyncDisposable
{
    private readonly IGatewayConnection _connection;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _loop;
    public event Action<GatewayEnvelope>? Event;
    public GatewaySession(IGatewayConnection connection) => _connection = connection;

    public async Task StartAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync(endpoint, cancellationToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await using var reader = _connection.ReadAllAsync(linked.Token).GetAsyncEnumerator(linked.Token);
        if (!await reader.MoveNextAsync() || reader.Current.Op != "hello")
            throw new InvalidDataException("Gateway did not send hello.");
        var hello = reader.Current.Data.Deserialize(ProtocolJson.Default.GatewayHello);
        var interval = Math.Clamp(hello?.HeartbeatIntervalMs ?? 30_000, 5_000, 60_000);
        var identify = JsonSerializer.SerializeToElement(new GatewayIdentify(ProtocolVersion.Current, accessToken), ProtocolJson.Default.GatewayIdentify);
        await _connection.SendAsync(new GatewayEnvelope("identify", null, null, identify), linked.Token);
        _loop = RunAsync(reader, interval, linked.Token);
    }

    private async Task RunAsync(IAsyncEnumerator<GatewayEnvelope> reader, int intervalMs, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
        var receiving = reader.MoveNextAsync().AsTask();
        var beating = timer.WaitForNextTickAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(receiving, beating);
            if (completed == receiving)
            {
                if (!await receiving) break;
                Event?.Invoke(reader.Current);
                receiving = reader.MoveNextAsync().AsTask();
            }
            else
            {
                if (!await beating) break;
                await _connection.SendAsync(new GatewayEnvelope("heartbeat", null, null, JsonDocument.Parse("{}").RootElement.Clone()), cancellationToken);
                beating = timer.WaitForNextTickAsync(cancellationToken).AsTask();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { }
        await _connection.DisposeAsync();
        _lifetime.Dispose();
    }
}
