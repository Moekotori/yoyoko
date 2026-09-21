using Chat.Core.Voice;

namespace Chat.Media;

[Flags]
public enum MediaCapabilities { None = 0, Voice = 1, ScreenShare = 2 }
public interface IMediaService : IVoiceMedia
{
    MediaCapabilities Capabilities { get; }
}
// Not implemented yet. Never load native libraries or start a worker while idle.
public sealed class UnavailableMediaService : IMediaService
{
    public MediaCapabilities Capabilities => MediaCapabilities.None;
    public bool Available => false;
    public event Action<string>? Faulted { add { } remove { } }
    public event Action<IReadOnlyList<string>>? SpeakingChanged { add { } remove { } }
    public Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException("Not implemented yet: LiveKit media worker is not available."));
    public Task LeaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetQualityAsync(AudioCaptureOptions audio, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<AudioDeviceList> ListDevicesAsync(CancellationToken cancellationToken)
        => Task.FromResult(new AudioDeviceList([], []));
    public Task SetDevicesAsync(AudioRoute route, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartLoopbackAsync(AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException("Not implemented yet: LiveKit media worker is not available."));
    public Task StopLoopbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
