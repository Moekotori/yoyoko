using Chat.Protocol;

namespace Chat.Core.Realtime;

public enum GatewayState { Disconnected, Connecting, Connected, Resuming, Faulted }
public interface IGatewayConnection : IAsyncDisposable
{
    GatewayState State { get; }
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);
    Task SendAsync(GatewayEnvelope envelope, CancellationToken cancellationToken);
    IAsyncEnumerable<GatewayEnvelope> ReadAllAsync(CancellationToken cancellationToken);
}

// Transport plus client resume: ignore seq ≤ cursor, reconnect on a gap, identify after invalid_session.
public sealed record ResumeCursor(string SessionId, long LastCommittedSequence);
public static class ReconnectPolicy
{
    public static TimeSpan Delay(int attempt, double jitter)
        => TimeSpan.FromMilliseconds(Math.Min(30_000, 500 * Math.Pow(2, Math.Clamp(attempt, 0, 6)))
            * (0.75 + Math.Clamp(jitter, 0, 1) * 0.5));
}
