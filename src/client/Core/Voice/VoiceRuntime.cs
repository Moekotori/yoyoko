using Chat.Core.Sessions;
using Chat.Protocol;

namespace Chat.Core.Voice;

public sealed class VoiceRuntime : IAsyncDisposable
{
    private readonly IChatApi _api;
    private readonly IVoiceMedia _media;
    private readonly Guid _selfUserId;
    private readonly Dictionary<Guid, VoiceStateDto> _states = [];
    private readonly HashSet<Guid> _speaking = [];
    private CancellationTokenSource _cancel = new();
    private bool _resync;
    private bool _muteBeforeDeaf;
    private bool _stopping;
    private int _reconnects;
    public VoiceRuntime(IChatApi api, IVoiceMedia media, Guid selfUserId)
    {
        _api = api;
        _media = media;
        _selfUserId = selfUserId;
        _media.Faulted += OnFaulted;
        _media.SpeakingChanged += OnSpeaking;
    }
    public Guid? ChannelId { get; private set; }
    public bool SelfMute { get; private set; }
    public bool SelfDeaf { get; private set; }
    public bool Joined => ChannelId is not null;
    public bool Reconnecting { get; private set; }
    public string? MediaError { get; private set; }
    public string Preferred { get; private set; } = AudioQualities.Studio;
    public string Quality { get; private set; } = AudioQualities.Studio;
    public string MaxQuality { get; private set; } = AudioQualities.Studio;
    public AudioRoute Route { get; private set; } = AudioRoute.System;
    public AudioCaptureOptions Capture => AudioQualities.Profile(Quality);
    public IEnumerable<VoiceStateDto> Participants => _states.Values;
    public event Action? Changed;
    public bool IsSpeaking(Guid userId) => _speaking.Contains(userId);
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
        if (state.UserId == _selfUserId && ChannelId is Guid current && state.ChannelId == current
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
        if (ChannelId is Guid current && current != channelId)
        {
            try { await _media.LeaveAsync(cancellationToken); } catch (Exception) { }
        }
        var joined = await _api.JoinVoiceAsync(channelId, SelfMute, SelfDeaf, Preferred, cancellationToken);
        ApplyJoined(joined);
        _resync = false;
        await AttachAsync(joined, cancellationToken);
    }

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        Reconnecting = false;
        try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
        try { await _media.LeaveAsync(cancellationToken); } catch (Exception) { }
        try { await _api.LeaveVoiceAsync(cancellationToken); } catch (ChatApiException) { }
        ChannelId = null;
        MediaError = null;
        _resync = false;
        _speaking.Clear();
        _stopping = false;
        ResetCancel();
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
        var state = await _api.PatchVoiceAsync(muted, SelfDeaf && muted, Preferred, cancellationToken);
        SelfMute = state.SelfMute;
        SelfDeaf = state.SelfDeaf;
        Apply(state);
        try
        {
            await _media.SetDeafenedAsync(SelfDeaf, cancellationToken);
            await _media.SetMutedAsync(SelfMute, cancellationToken);
            MediaError = null;
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
        var state = await _api.PatchVoiceAsync(muted, deafened, Preferred, cancellationToken);
        if (deafened) _muteBeforeDeaf = previousMute;
        SelfMute = state.SelfMute;
        SelfDeaf = state.SelfDeaf;
        Apply(state);
        try
        {
            await _media.SetDeafenedAsync(SelfDeaf, cancellationToken);
            await _media.SetMutedAsync(SelfMute, cancellationToken);
            MediaError = null;
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
        try { await _media.SetDevicesAsync(route, cancellationToken); MediaError = null; }
        catch (Exception exception) { Route = previous; MediaError = exception.Message; throw; }
        finally { Changed?.Invoke(); }
    }

    public Task<AudioDeviceList> ListDevicesAsync(CancellationToken cancellationToken)
        => _media.ListDevicesAsync(cancellationToken);

    public async Task SetQualityAsync(string quality, CancellationToken cancellationToken)
    {
        Preferred = quality;
        if (!Joined)
        {
            Quality = AudioQualities.Clamp(quality, MaxQuality);
            Changed?.Invoke();
            return;
        }
        var state = await _api.PatchVoiceAsync(SelfMute, SelfDeaf, Preferred, cancellationToken);
        Quality = string.IsNullOrEmpty(state.AudioQuality) ? AudioQualities.Clamp(Preferred, MaxQuality) : state.AudioQuality;
        Apply(state);
        try { await _media.SetQualityAsync(AudioQualities.Profile(Quality), cancellationToken); MediaError = null; }
        catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public async Task SetChannelMaxQualityAsync(Guid channelId, string quality, CancellationToken cancellationToken)
    {
        var updated = await _api.PatchChannelAsync(channelId, new PatchChannelRequest(quality), cancellationToken);
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

    public ValueTask DisposeAsync()
    {
        _media.Faulted -= OnFaulted;
        _media.SpeakingChanged -= OnSpeaking;
        return new(LeaveAsync(CancellationToken.None));
    }

    private void ApplyJoined(VoiceJoinDto joined)
    {
        ChannelId = joined.State.ChannelId;
        SelfMute = joined.State.SelfMute;
        SelfDeaf = joined.State.SelfDeaf;
        Quality = string.IsNullOrEmpty(joined.Audio.Id) ? AudioQualities.Studio : joined.Audio.Id;
        MaxQuality = string.IsNullOrEmpty(joined.MaxAudioQuality) ? AudioQualities.Studio : joined.MaxAudioQuality;
        Quality = AudioQualities.Clamp(Quality, MaxQuality);
        Apply(joined.State);
    }

    private async Task AttachAsync(VoiceJoinDto joined, CancellationToken cancellationToken)
    {
        var capture = new AudioCaptureOptions(joined.Audio.Id, joined.Audio.SampleRateHz, joined.Audio.Channels,
            joined.Audio.BitrateBps, joined.Audio.FrameMs, joined.Audio.Dtx, joined.Audio.Fec);
        try
        {
            await _media.ConnectAsync(joined.Rtc.Url, joined.Rtc.Token, SelfMute, SelfDeaf, capture, Route, cancellationToken);
            MediaError = null;
            Reconnecting = false;
        }
        catch (Exception exception)
        {
            MediaError = exception.Message;
            StartReconnect();
        }
        Changed?.Invoke();
    }

    private void OnFaulted(string reason)
    {
        if (_stopping || !Joined) return;
        MediaError = reason;
        Changed?.Invoke();
        StartReconnect();
    }

    private void OnSpeaking(IReadOnlyList<string> ids)
    {
        var next = new HashSet<Guid>();
        foreach (var id in ids)
            if (Guid.TryParse(id, out var guid)) next.Add(guid);
        if (next.SetEquals(_speaking)) return;
        _speaking.Clear();
        foreach (var guid in next) _speaking.Add(guid);
        Changed?.Invoke();
    }

    private void StartReconnect()
    {
        if (_stopping || !Joined || Interlocked.CompareExchange(ref _reconnects, 1, 0) != 0) return;
        Reconnecting = true;
        Changed?.Invoke();
        _ = ReconnectAsync();
    }

    private async Task ReconnectAsync()
    {
        try
        {
            for (var attempt = 1; attempt <= 5 && !_stopping && Joined; attempt++)
            {
                try { await Task.Delay(Math.Min(1_000 * (1 << (attempt - 1)), 8_000), _cancel.Token); }
                catch (OperationCanceledException) { return; }
                if (_stopping || ChannelId is not Guid channel) return;
                try
                {
                    var joined = await _api.JoinVoiceAsync(channel, SelfMute, SelfDeaf, Preferred, _cancel.Token);
                    ApplyJoined(joined);
                    var capture = new AudioCaptureOptions(joined.Audio.Id, joined.Audio.SampleRateHz, joined.Audio.Channels,
                        joined.Audio.BitrateBps, joined.Audio.FrameMs, joined.Audio.Dtx, joined.Audio.Fec);
                    await _media.ConnectAsync(joined.Rtc.Url, joined.Rtc.Token, SelfMute, SelfDeaf, capture, Route, _cancel.Token);
                    MediaError = null;
                    Reconnecting = false;
                    Changed?.Invoke();
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception exception)
                {
                    MediaError = exception.Message;
                    Changed?.Invoke();
                }
            }
            Reconnecting = false;
            Changed?.Invoke();
        }
        finally { Interlocked.Exchange(ref _reconnects, 0); }
    }

    private void ResetCancel()
    {
        try { _cancel.Dispose(); } catch (ObjectDisposedException) { }
        _cancel = new();
    }
}
