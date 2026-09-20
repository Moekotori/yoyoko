using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Chat.Core.Instances;
using Chat.Localization;
using Chat.Media;
using Chat.Networking.Http;
using Chat.Networking.WebSocket;
using Chat.Storage;
using Chat.UI.Localization;
using Chat.UI.Shell;

namespace Chat.App;

public partial class DesktopApplication : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private HttpClient? _http;
    private InstanceManager? _instances;
    private I18n? _i18n;
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
            var media = WorkerMediaService.Create();
            var locale = FileLocalePreference.Load(settings.PreferencesPath);
            _i18n = new I18n(new TextCatalog(), locale);
            I18n.Use(_i18n);
            var shell = new ShellViewModel(_instances, cache, vault, new HttpInstanceDiscovery(_http),
                new ChatApiFactory(), () => new WebSocketConnection(), new MediaVoiceAdapter(media),
                locale, locale, _i18n, settings.ProductName, _lifetime.Token);
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await shell.InitializeAsync(cache.InitializeAsync);
            desktop.Exit += (_, _) =>
            {
                _lifetime.Cancel();
                shell.Dispose();
                _i18n.Dispose();
                _instances.DisposeAsync().AsTask().GetAwaiter().GetResult();
                media.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _http.Dispose();
                _lifetime.Dispose();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
