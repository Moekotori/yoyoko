using Avalonia.Threading;
using Chat.Core.Messaging;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private CancellationTokenSource? _channelLoad;
    private bool _channelLoading;
    private int _channelContentVersion;
    public bool IsChannelLoading => _channelLoading;
    public int ChannelContentVersion => _channelContentVersion;

    private void SetChannelLoading(bool value)
    {
        if (_channelLoading == value) return;
        _channelLoading = value;
        Changed(nameof(IsChannelLoading));
        Changed(nameof(EmptyMessages));
        Changed(nameof(HasOlder));
    }

    private async Task OpenChannelAsync()
    {
        var session = SelectedInstance?.Context.Session;
        var channel = SelectedChannel;
        DetachTimeline();
        SetChannelLoading(session is not null && channel?.Kind == "text");
        Messages.Clear();
        RefreshMessagePresentation();
        if (session is null || channel is null) return;
        if (channel.Kind == "voice")
        {
            try
            {
                session.Voice.ApplyRoute(Devices.Route);
                await Devices.StopLoopbackAsync();
                await session.Voice.JoinAsync(channel.Id, SelectedAudioQuality?.Id, _lifetime);
                NotifyVoice();
                _ = Devices.RefreshAsync();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error) { OnError(error); }
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        _channelLoad = cancellation;
        var timeline = session.OpenChannel(channel.Id);
        _timeline = timeline;
        // Both cached and remote snapshots belong to this selection. A queued callback
        // from an abandoned channel must never update or scroll the current one.
        _timelineChanged = () => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_timeline, timeline) || cancellation.IsCancellationRequested) return;
            var firstSnapshot = IsChannelLoading;
            SetChannelLoading(false);
            SyncMessages();
            if (firstSnapshot)
            {
                _channelContentVersion++;
                Changed(nameof(ChannelContentVersion));
                ScrollToLatest?.Invoke();
            }
        });
        timeline.Changed += _timelineChanged;
        try { await timeline.LoadLatestAsync(cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(_timeline, timeline))
            {
                SetChannelLoading(false);
                OnError(error);
            }
        }
    }

    private void DetachTimeline()
    {
        _previews.Clear();
        var cancellation = _channelLoad;
        _channelLoad = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (_timeline is not null && _timelineChanged is not null)
            _timeline.Changed -= _timelineChanged;
        _timeline?.ReleaseUnsent();
        _timeline = null;
        _timelineChanged = null;
        SetChannelLoading(false);
    }
}
