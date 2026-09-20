using Avalonia;

namespace Chat.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
#endif
        AppBuilder.Configure<DesktopApplication>()
            .UsePlatformDetect()
#if DEBUG
            .LogToTrace(Avalonia.Logging.LogEventLevel.Information, "HotAvalonia")
#endif
            .StartWithClassicDesktopLifetime(args);
    }
}
