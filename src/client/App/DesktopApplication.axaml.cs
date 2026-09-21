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
using Chat.UI.Preview;

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
            if (desktop.Args?.Contains("--design-preview") == true)
            {
                var previewLocale = FileLocalePreference.Load(settings.PreferencesPath);
                I18n.Attach(new TextCatalog(), previewLocale);
                _i18n = I18n.Presenter;
                desktop.MainWindow = new DesignPreviewWindow(settings.ProductName, _i18n);
                desktop.Exit += (_, _) => { _i18n.Dispose(); _lifetime.Dispose(); };
                base.OnFrameworkInitializationCompleted();
                return;
            }
            _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
            var cache = new SqliteCache(settings.CachePath);
            var vault = new FileCredentialVault(Path.GetDirectoryName(settings.CachePath)!);
            _instances = new(cache, new HttpInstanceDiscovery(_http));
            var media = WorkerMediaService.Create();
            var locale = FileLocalePreference.Load(settings.PreferencesPath);
            I18n.Attach(new TextCatalog(), locale);
            _i18n = I18n.Presenter;
            var apis = new ChatApiFactory();
            var voice = new MediaVoiceAdapter(media);
            var defaultAddress = desktop.Args?.Contains("--local-workspace") == true
                ? "http://localhost:8080" : settings.DefaultInstanceUrl;
            var connection = new WorkspaceConnection(_instances, cache, vault, apis,
                () => new WebSocketConnection(), voice, defaultAddress);
            var shell = new ShellViewModel(_instances, cache, vault, new HttpInstanceDiscovery(_http),
                apis, () => new WebSocketConnection(), voice,
                locale, locale, locale, _i18n, settings.ProductName, _lifetime.Token, connection);
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            var workspace = new DefaultWorkspace(connection);
            window.Opened += async (_, _) => await shell.InitializeAsync(cache.InitializeAsync,
                workspace.PrepareAsync);
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
