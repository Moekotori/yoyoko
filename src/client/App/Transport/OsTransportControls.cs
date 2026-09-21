using Chat.Core.Voice;

namespace Chat.App.Transport;

internal static class OsTransportControls
{
    public static ISystemTransportControls Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsSmtcControls();
        if (OperatingSystem.IsMacOS()) return new MacNowPlayingControls();
        if (OperatingSystem.IsLinux()) return new LinuxMprisControls();
        return NullSystemTransportControls.Instance;
    }
}
