using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Chat.Core.Realtime;
using Chat.Networking.Http;
using Chat.Protocol;

namespace Chat.Networking.WebSocket;

// One receive consumer and serialized sends per instance. No unbounded event queue.
public sealed class WebSocketConnection : IGatewayConnection
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    public GatewayState State { get; private set; } = GatewayState.Disconnected;

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        State = GatewayState.Connecting;
        try
        {
            _socket.Options.Proxy = ClientHttp.Proxy;
            await _socket.ConnectAsync(endpoint, cancellationToken);
            State = GatewayState.Connected;
        }
        catch { State = GatewayState.Faulted; throw; }
    }
    public async Task SendAsync(GatewayEnvelope envelope, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJson.Default.GatewayEnvelope);
        if (bytes.Length > ProtocolVersion.MaxGatewayBytes) throw new InvalidDataException("Gateway frame too large.");
        await _sendLock.WaitAsync(cancellationToken);
        try { await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, cancellationToken); }
        finally { _sendLock.Release(); }
    }
    public async IAsyncEnumerable<GatewayEnvelope> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[ProtocolVersion.MaxGatewayBytes];
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var count = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (count == buffer.Length) throw new InvalidDataException("Gateway frame too large.");
                    result = await _socket.ReceiveAsync(buffer.AsMemory(count), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close) yield break;
                    if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Expected JSON text.");
                    count += result.Count;
                } while (!result.EndOfMessage);
                yield return JsonSerializer.Deserialize(buffer.AsSpan(0, count), ProtocolJson.Default.GatewayEnvelope)
                    ?? throw new InvalidDataException("Empty gateway event.");
            }
        }
        finally { State = GatewayState.Disconnected; }
    }
    public ValueTask DisposeAsync()
    {
        // Abort bounds shutdown even if the remote never responds to a close handshake.
        _socket.Abort();
        _socket.Dispose();
        _sendLock.Dispose();
        State = GatewayState.Disconnected;
        return ValueTask.CompletedTask;
    }
}
