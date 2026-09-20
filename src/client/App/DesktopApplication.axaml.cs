using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Chat.Core.Instances;
using Chat.Networking.Http;
using Chat.Networking.WebSocket;
using Chat.Storage;
using Chat.UI.Shell;

namespace Chat.App;

public partial class DesktopApplication : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private HttpClient? _http;
    private InstanceManager? _instances;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load();
            _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
            var cache = new SqliteCache(settings.CachePath);
            var vault = new FileCredentialVault(Path.GetDirectoryName(settings.CachePath)!);
            _instances = new(cache, new HttpInstanceDiscovery(_http));
            var shell = new ShellViewModel(_instances, cache, vault, new HttpInstanceDiscovery(_http),
                new ChatApiFactory(), () => new WebSocketConnection(), settings.ProductName, _lifetime.Token);
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await shell.InitializeAsync(cache.InitializeAsync);
            desktop.Exit += (_, _) =>
            {
                _lifetime.Cancel();
                _instances.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _http.Dispose();
                _lifetime.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
