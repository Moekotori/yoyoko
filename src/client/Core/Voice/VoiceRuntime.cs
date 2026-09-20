using Chat.Core.Sessions;
using Chat.Protocol;

namespace Chat.Core.Voice;

public sealed class VoiceRuntime(IChatApi api, IVoiceMedia media) : IAsyncDisposable
{
    private readonly Dictionary<Guid, VoiceStateDto> _states = [];
    public Guid? ChannelId { get; private set; }
    public bool SelfMute { get; private set; }
    public bool SelfDeaf { get; private set; }
    public bool Joined => ChannelId is not null;
    public string? MediaError { get; private set; }
    public string Quality { get; private set; } = AudioQualities.Studio;
    public string MaxQuality { get; private set; } = AudioQualities.Studio;
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
        Changed?.Invoke();
    }

    public Task JoinAsync(Guid channelId, CancellationToken cancellationToken)
        => JoinAsync(channelId, Quality, cancellationToken);

    public async Task JoinAsync(Guid channelId, string? quality, CancellationToken cancellationToken)
    {
        var joined = await api.JoinVoiceAsync(channelId, SelfMute, SelfDeaf, quality ?? Quality, cancellationToken);
        ChannelId = joined.State.ChannelId;
        SelfMute = joined.State.SelfMute;
        SelfDeaf = joined.State.SelfDeaf;
        Quality = string.IsNullOrEmpty(joined.Audio.Id) ? AudioQualities.Studio : joined.Audio.Id;
        MaxQuality = string.IsNullOrEmpty(joined.MaxAudioQuality) ? AudioQualities.Studio : joined.MaxAudioQuality;
        Apply(joined.State);
        MediaError = null;
        var capture = new AudioCaptureOptions(joined.Audio.Id, joined.Audio.SampleRateHz, joined.Audio.Channels,
            joined.Audio.BitrateBps, joined.Audio.FrameMs, joined.Audio.Dtx, joined.Audio.Fec);
        try { await media.ConnectAsync(joined.Rtc.Url, joined.Rtc.Token, SelfMute, SelfDeaf, capture, cancellationToken); }
        catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        try { await media.LeaveAsync(cancellationToken); } catch (Exception) { }
        try { await api.LeaveVoiceAsync(cancellationToken); } catch (ChatApiException) { }
        ChannelId = null;
        MediaError = null;
        Changed?.Invoke();
    }

    public async Task SetMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        if (!Joined) return;
        var state = await api.PatchVoiceAsync(muted, SelfDeaf && muted, Quality, cancellationToken);
        SelfMute = state.SelfMute;
        SelfDeaf = state.SelfDeaf;
        Apply(state);
        try { await media.SetMutedAsync(SelfMute, cancellationToken); }
        catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public async Task SetDeafAsync(bool deafened, CancellationToken cancellationToken)
    {
        if (!Joined) return;
        var state = await api.PatchVoiceAsync(deafened || SelfMute, deafened, Quality, cancellationToken);
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

    public async Task SetQualityAsync(string quality, CancellationToken cancellationToken)
    {
        if (!Joined || ChannelId is not Guid channel) { Quality = quality; Changed?.Invoke(); return; }
        await JoinAsync(channel, quality, cancellationToken);
    }

    public async Task SetChannelMaxQualityAsync(Guid channelId, string quality, CancellationToken cancellationToken)
    {
        var updated = await api.PatchChannelAsync(channelId, new PatchChannelRequest(quality), cancellationToken);
        MaxQuality = updated.AudioQuality ?? quality;
        if (Joined && ChannelId == channelId)
            await SetQualityAsync(AudioQualities.Clamp(Quality, MaxQuality), cancellationToken);
        else Changed?.Invoke();
    }

    public ValueTask DisposeAsync() => new(LeaveAsync(CancellationToken.None));
}
