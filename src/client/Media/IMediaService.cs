namespace Chat.Media;

[Flags]
public enum MediaCapabilities { None = 0, Voice = 1, ScreenShare = 2 }
public interface IMediaService : IAsyncDisposable
{
    MediaCapabilities Capabilities { get; }
    Task JoinVoiceAsync(Uri endpoint, string token, CancellationToken cancellationToken);
    Task LeaveVoiceAsync(CancellationToken cancellationToken);
}
// Not implemented yet. Never load native libraries or start a worker while idle.
public sealed class UnavailableMediaService : IMediaService
{
    public MediaCapabilities Capabilities => MediaCapabilities.None;
    public Task JoinVoiceAsync(Uri endpoint, string token, CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException("Not implemented yet: LiveKit native media worker."));
    public Task LeaveVoiceAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
