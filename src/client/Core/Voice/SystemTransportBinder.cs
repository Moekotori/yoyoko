using Chat.Core.Sessions;

namespace Chat.Core.Voice;

public sealed class SystemTransportBinder : IDisposable
{
    private readonly ISystemTransportControls _controls;
    private readonly ISystemTransportPreference _preference;
    private readonly string _productName;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly object _gate = new();
    private readonly HashSet<InstanceSession> _sessions = [];
    private readonly Dictionary<InstanceSession, Action> _handlers = [];
    private TransportNowPlaying? _last;
    private bool _disposed;

    public SystemTransportBinder(ISystemTransportControls controls, ISystemTransportPreference preference, string productName)
    {
        _controls = controls;
        _preference = preference;
        _productName = productName;
        _preference.Changed += RequestPublish;
        _controls.CommandRequested += OnCommand;
    }

    public void BindWindow(nint hwnd) => _controls.BindWindow(hwnd);

    public void Attach(InstanceSession session)
    {
        lock (_gate)
        {
            if (_disposed || !_sessions.Add(session)) return;
            void OnChanged() => RequestPublish();
            _handlers[session] = OnChanged;
            session.Voice.Changed += OnChanged;
            session.CommunityChanged += OnChanged;
        }
        RequestPublish();
    }

    public void Detach(InstanceSession session)
    {
        lock (_gate)
        {
            if (!_sessions.Remove(session)) return;
            if (_handlers.Remove(session, out var onChanged))
            {
                session.Voice.Changed -= onChanged;
                session.CommunityChanged -= onChanged;
            }
        }
        RequestPublish();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var (session, onChanged) in _handlers)
            {
                session.Voice.Changed -= onChanged;
                session.CommunityChanged -= onChanged;
            }
            _handlers.Clear();
            _sessions.Clear();
        }
        _preference.Changed -= RequestPublish;
        _controls.CommandRequested -= OnCommand;
        _last = null;
        _controls.Publish(null);
    }

    private void RequestPublish()
    {
        if (_context is null || ReferenceEquals(_context, SynchronizationContext.Current))
            Publish();
        else
            _context.Post(_ => Publish(), null);
    }

    private void Publish()
    {
        InstanceSession? session;
        lock (_gate)
        {
            if (_disposed || !_preference.HeadsetMediaKeys)
            {
                Push(null);
                return;
            }
            session = _sessions.FirstOrDefault(item => item.Voice.Joined);
        }
        if (session is null)
        {
            Push(null);
            return;
        }
        var channel = session.Channels.FirstOrDefault(item => item.Id == session.Voice.ChannelId);
        var serverId = channel?.ServerId
            ?? session.Voice.Participants.FirstOrDefault(item => item.ChannelId == session.Voice.ChannelId)?.ServerId;
        var server = serverId is Guid id ? session.Servers.FirstOrDefault(item => item.Id == id) : null;
        Push(new(
            _productName,
            server?.Name ?? session.Descriptor.DisplayName,
            channel?.Name ?? "",
            session.Voice.SelfMute || session.Voice.SelfDeaf));
    }

    private void Push(TransportNowPlaying? session)
    {
        if (Equals(_last, session)) return;
        _last = session;
        _controls.Publish(session);
    }

    private void OnCommand(TransportCommand command)
    {
        if (_context is null)
            Apply(command).GetAwaiter().GetResult();
        else
            _context.Post(_ => _ = Apply(command), null);
    }

    private async Task Apply(TransportCommand command)
    {
        InstanceSession? session;
        lock (_gate) session = _sessions.FirstOrDefault(item => item.Voice.Joined);
        if (session is null) return;
        var mute = command switch
        {
            TransportCommand.Mute => true,
            TransportCommand.Unmute => false,
            _ => !session.Voice.SelfMute
        };
        try { await session.Voice.SetMuteAsync(mute, CancellationToken.None); }
        catch (Exception) { }
    }
}
