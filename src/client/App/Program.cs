using Avalonia;

namespace Chat.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => AppBuilder.Configure<DesktopApplication>()
        .UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}
