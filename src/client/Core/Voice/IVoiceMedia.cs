namespace Chat.Core.Voice;

public interface IVoiceMedia : IAsyncDisposable
{
    bool Available { get; }
    Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken);
    Task LeaveAsync(CancellationToken cancellationToken);
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken);
    Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken);
    Task SetQualityAsync(AudioCaptureOptions audio, CancellationToken cancellationToken);
    Task<AudioDeviceList> ListDevicesAsync(CancellationToken cancellationToken);
    Task SetDevicesAsync(AudioRoute route, CancellationToken cancellationToken);
    event Action<string>? Faulted;
}
