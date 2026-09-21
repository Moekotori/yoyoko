using Chat.Core.Sessions;
using Chat.Protocol;

namespace Chat.Core.Voice;

public sealed class VoiceRuntime(IChatApi api, IVoiceMedia media, Guid selfUserId) : IAsyncDisposable
{
    private readonly Dictionary<Guid, VoiceStateDto> _states = [];
    private bool _resync;
    private bool _muteBeforeDeaf;
    public Guid? ChannelId { get; private set; }
    public bool SelfMute { get; private set; }
    public bool SelfDeaf { get; private set; }
    public bool Joined => ChannelId is not null;
    public string? MediaError { get; private set; }
    public string Preferred { get; private set; } = AudioQualities.Studio;
    public string Quality { get; private set; } = AudioQualities.Studio;
    public string MaxQuality { get; private set; } = AudioQualities.Studio;
    public AudioRoute Route { get; private set; } = AudioRoute.System;
    public AudioCaptureOptions Capture => AudioQualities.Profile(Quality);
    public IEnumerable<VoiceStateDto> Participants => _states.Values;
    public event Action? Changed;
    public IEnumerable<VoiceStateDto> InChannel(Guid channelId) =>
        _states.Values.Where(state => state.ChannelId == channelId);

    public void Replace(IEnumerable<VoiceStateDto>? states)
    {
        _states.Clear();
        if (states is null) return;
        foreach (var state in states)
            if (state.ChannelId is not null) _states[state.UserId] = state;
        Changed?.Invoke();
    }

    public void Apply(VoiceStateDto state)
    {
        if (state.ChannelId is null) _states.Remove(state.UserId);
        else _states[state.UserId] = state;
        if (state.UserId == selfUserId && ChannelId is Guid current && state.ChannelId == current
            && !string.IsNullOrEmpty(state.AudioQuality))
        {
            var incoming = AudioQualities.Clamp(state.AudioQuality, MaxQuality);
            if (incoming != Quality)
            {
                Quality = incoming;
                _resync = true;
            }
        }
        Changed?.Invoke();
    }

    public Task JoinAsync(Guid channelId, CancellationToken cancellationToken)
        => JoinAsync(channelId, Preferred, cancellationToken);

    public async Task JoinAsync(Guid channelId, string? quality, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(quality)) Preferred = quality;
        if (ChannelId is not null)
        {
            try { await media.LeaveAsync(cancellationToken); } catch (Exception) { }
        }
        var joined = await api.JoinVoiceAsync(channelId, SelfMute, SelfDeaf, Preferred, cancellationToken);
        ChannelId = joined.State.ChannelId;
        SelfMute = joined.State.SelfMute;
        SelfDeaf = joined.State.SelfDeaf;
        Quality = string.IsNullOrEmpty(joined.Audio.Id) ? AudioQualities.Studio : joined.Audio.Id;
        MaxQuality = string.IsNullOrEmpty(joined.MaxAudioQuality) ? AudioQualities.Studio : joined.MaxAudioQuality;
        Quality = AudioQualities.Clamp(Quality, MaxQuality);
        Apply(joined.State);
        _resync = false;
        MediaError = null;
        var capture = new AudioCaptureOptions(joined.Audio.Id, joined.Audio.SampleRateHz, joined.Audio.Channels,
            joined.Audio.BitrateBps, joined.Audio.FrameMs, joined.Audio.Dtx, joined.Audio.Fec);
        try { await media.ConnectAsync(joined.Rtc.Url, joined.Rtc.Token, SelfMute, SelfDeaf, capture, Route, cancellationToken); }
        catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        try { await media.LeaveAsync(cancellationToken); } catch (Exception) { }
        try { await api.LeaveVoiceAsync(cancellationToken); } catch (ChatApiException) { }
        ChannelId = null;
        MediaError = null;
        _resync = false;
        Changed?.Invoke();
    }

    public async Task SetMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        if (!Joined)
        {
            SelfMute = muted;
            SelfDeaf = SelfDeaf && muted;
            Changed?.Invoke();
            return;
        }
        var state = await api.PatchVoiceAsync(muted, SelfDeaf && muted, Preferred, cancellationToken);
        SelfMute = state.SelfMute;
        SelfDeaf = state.SelfDeaf;
        Apply(state);
        try
        {
            await media.SetDeafenedAsync(SelfDeaf, cancellationToken);
            await media.SetMutedAsync(SelfMute, cancellationToken);
        }
        catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public async Task SetDeafAsync(bool deafened, CancellationToken cancellationToken)
    {
        if (deafened == SelfDeaf) return;
        var previousMute = SelfMute;
        var muted = deafened || _muteBeforeDeaf;
        if (!Joined)
        {
            if (deafened) _muteBeforeDeaf = previousMute;
            SelfMute = muted;
            SelfDeaf = deafened;
            Changed?.Invoke();
            return;
        }
        var state = await api.PatchVoiceAsync(muted, deafened, Preferred, cancellationToken);
        if (deafened) _muteBeforeDeaf = previousMute;
        SelfMute = state.SelfMute;
        SelfDeaf = state.SelfDeaf;
        Apply(state);
        try
        {
            await media.SetDeafenedAsync(SelfDeaf, cancellationToken);
            await media.SetMutedAsync(SelfMute, cancellationToken);
        }
        catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public void ApplyRoute(AudioRoute route)
    {
        Route = route;
        Changed?.Invoke();
    }

    public async Task SetRouteAsync(AudioRoute route, CancellationToken cancellationToken)
    {
        var previous = Route;
        Route = route;
        if (!Joined) { Changed?.Invoke(); return; }
        try { await media.SetDevicesAsync(route, cancellationToken); MediaError = null; }
        catch (Exception exception) { Route = previous; MediaError = exception.Message; throw; }
        finally { Changed?.Invoke(); }
    }

    public Task<AudioDeviceList> ListDevicesAsync(CancellationToken cancellationToken)
        => media.ListDevicesAsync(cancellationToken);

    public async Task SetQualityAsync(string quality, CancellationToken cancellationToken)
    {
        Preferred = quality;
        if (!Joined)
        {
            Quality = AudioQualities.Clamp(quality, MaxQuality);
            Changed?.Invoke();
            return;
        }
        var state = await api.PatchVoiceAsync(SelfMute, SelfDeaf, Preferred, cancellationToken);
        Quality = string.IsNullOrEmpty(state.AudioQuality) ? AudioQualities.Clamp(Preferred, MaxQuality) : state.AudioQuality;
        Apply(state);
        try { await media.SetQualityAsync(AudioQualities.Profile(Quality), cancellationToken); }
        catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public async Task SetChannelMaxQualityAsync(Guid channelId, string quality, CancellationToken cancellationToken)
    {
        var updated = await api.PatchChannelAsync(channelId, new PatchChannelRequest(quality), cancellationToken);
        await ApplyChannelMaxAsync(channelId, updated.AudioQuality ?? quality, cancellationToken);
    }

    public async Task ApplyChannelMaxAsync(Guid channelId, string? max, CancellationToken cancellationToken)
    {
        var cap = string.IsNullOrEmpty(max) ? AudioQualities.Studio : max;
        var next = AudioQualities.Clamp(Preferred, cap);
        if (MaxQuality == cap && Quality == next) return;
        MaxQuality = cap;
        if (Joined && ChannelId == channelId && next != Quality)
            await SetQualityAsync(Preferred, cancellationToken);
        else
        {
            Quality = next;
            Changed?.Invoke();
        }
    }

    public async Task SyncEncoderAsync(CancellationToken cancellationToken)
    {
        if (!_resync || ChannelId is null) return;
        _resync = false;
        await SetQualityAsync(Preferred, cancellationToken);
    }

    public ValueTask DisposeAsync() => new(LeaveAsync(CancellationToken.None));
}
