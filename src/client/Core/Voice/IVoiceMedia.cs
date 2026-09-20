namespace Chat.Core.Voice;

public interface IVoiceMedia : IAsyncDisposable
{
    bool Available { get; }
    Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, CancellationToken cancellationToken);
    Task LeaveAsync(CancellationToken cancellationToken);
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken);
    Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken);
    event Action<string>? Faulted;
}
