namespace Chat.Core.Voice;

public sealed class UnavailableVoiceMedia : IVoiceMedia
{
    public bool Available => false;
    public event Action<string>? Faulted { add { } remove { } }
    public Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, AudioCaptureOptions audio, CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException("Not implemented yet: LiveKit media worker is not available."));
    public Task LeaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetQualityAsync(AudioCaptureOptions audio, CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
