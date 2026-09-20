using Chat.Core.Api;
using Chat.Protocol;

namespace Chat.Core.Voice;

public sealed class VoiceRuntime(IChatApi api, IVoiceMedia media, Uri apiRoot, string accessToken) : IAsyncDisposable
{
    private readonly Dictionary<Guid, VoiceStateDto> _states = [];
    public Guid? ChannelId { get; private set; }
    public bool SelfMute { get; private set; }
    public bool SelfDeaf { get; private set; }
    public bool Joined => ChannelId is not null;
    public string? MediaError { get; private set; }
    public IReadOnlyCollection<VoiceStateDto> Participants => _states.Values;
    public event Action? Changed;

    public IEnumerable<VoiceStateDto> InChannel(Guid channelId)
        => _states.Values.Where(state => state.ChannelId == channelId);

    public void Replace(IEnumerable<VoiceStateDto> states)
    {
        _states.Clear();
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

    public async Task JoinAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var joined = await api.JoinVoiceAsync(apiRoot, accessToken, channelId, SelfMute, SelfDeaf, cancellationToken);
        ChannelId = joined.State.ChannelId;
        SelfMute = joined.State.SelfMute;
        SelfDeaf = joined.State.SelfDeaf;
        Apply(joined.State);
        MediaError = null;
        try
        {
            await media.ConnectAsync(joined.Rtc.Url, joined.Rtc.Token, SelfMute, SelfDeaf, cancellationToken);
        }
        catch (Exception exception)
        {
            MediaError = exception.Message;
        }
        Changed?.Invoke();
    }

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        try { await media.LeaveAsync(cancellationToken); } catch (Exception) { }
        try { await api.LeaveVoiceAsync(apiRoot, accessToken, cancellationToken); } catch (ChatApiException) { }
        if (ChannelId is Guid channel)
        {
            foreach (var id in _states.Where(pair => pair.Value.ChannelId == channel).Select(pair => pair.Key).ToArray())
                _states.Remove(id);
        }
        ChannelId = null;
        MediaError = null;
        Changed?.Invoke();
    }

    public async Task SetMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        if (!Joined) return;
        var state = await api.PatchVoiceAsync(apiRoot, accessToken, muted, SelfDeaf && muted, cancellationToken);
        SelfMute = state.SelfMute;
        SelfDeaf = state.SelfDeaf;
        Apply(state);
        try { await media.SetMutedAsync(SelfMute, cancellationToken); } catch (Exception exception) { MediaError = exception.Message; }
        Changed?.Invoke();
    }

    public async Task SetDeafAsync(bool deafened, CancellationToken cancellationToken)
    {
        if (!Joined) return;
        var state = await api.PatchVoiceAsync(apiRoot, accessToken, deafened || SelfMute, deafened, cancellationToken);
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

    public ValueTask DisposeAsync() => LeaveAsync(CancellationToken.None);
}
