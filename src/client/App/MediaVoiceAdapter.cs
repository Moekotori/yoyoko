using Chat.Core.Voice;
using Chat.Media;

namespace Chat.App;

internal sealed class MediaVoiceAdapter(IMediaService media) : IVoiceMedia
{
    public bool Available => media.Available;
    public event Action<string>? Faulted
    {
        add => media.Faulted += value;
        remove => media.Faulted -= value;
    }
    public event Action<IReadOnlyList<string>>? SpeakingChanged
    {
        add => media.SpeakingChanged += value;
        remove => media.SpeakingChanged -= value;
    }
    public Task ConnectAsync(Uri endpoint, string token, bool muted, bool deafened, AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken)
        => media.ConnectAsync(endpoint, token, muted, deafened, audio, route, cancellationToken);
    public Task LeaveAsync(CancellationToken cancellationToken) => media.LeaveAsync(cancellationToken);
    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => media.SetMutedAsync(muted, cancellationToken);
    public Task SetDeafenedAsync(bool deafened, CancellationToken cancellationToken) => media.SetDeafenedAsync(deafened, cancellationToken);
    public Task SetQualityAsync(AudioCaptureOptions audio, CancellationToken cancellationToken)
        => media.SetQualityAsync(audio, cancellationToken);
    public Task<AudioDeviceList> ListDevicesAsync(CancellationToken cancellationToken)
        => media.ListDevicesAsync(cancellationToken);
    public Task SetDevicesAsync(AudioRoute route, CancellationToken cancellationToken)
        => media.SetDevicesAsync(route, cancellationToken);
    public Task StartLoopbackAsync(AudioCaptureOptions audio, AudioRoute route, CancellationToken cancellationToken)
        => media.StartLoopbackAsync(audio, route, cancellationToken);
    public Task StopLoopbackAsync(CancellationToken cancellationToken)
        => media.StopLoopbackAsync(cancellationToken);
    public ValueTask DisposeAsync() => media.DisposeAsync();
}
