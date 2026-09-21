namespace Chat.Core.Voice;

public enum TransportCommand { ToggleMute, Mute, Unmute }

public sealed record TransportNowPlaying(string AppName, string Community, string Channel, bool Muted);

public interface ISystemTransportPreference
{
    bool HeadsetMediaKeys { get; }
    void SetHeadsetMediaKeys(bool value);
    event Action? Changed;
}

public interface ISystemTransportControls : IDisposable
{
    bool Available { get; }
    void BindWindow(nint hwnd);
    void Publish(TransportNowPlaying? session);
    event Action<TransportCommand>? CommandRequested;
}

public sealed class NullSystemTransportControls : ISystemTransportControls
{
    public static NullSystemTransportControls Instance { get; } = new();
    public bool Available => false;
    public void BindWindow(nint hwnd) { }
    public void Publish(TransportNowPlaying? session) { }
    public event Action<TransportCommand>? CommandRequested { add { } remove { } }
    public void Dispose() { }
}
