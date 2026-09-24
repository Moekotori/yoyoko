using System.ComponentModel;
using Chat.Core;
using Chat.Core.Sessions;
using Chat.Localization;

namespace Chat.UI.Localization;

public sealed class I18n : INotifyPropertyChanged, IDisposable
{
    public static I18n Presenter { get; } = new(new TextCatalog(), new MemoryLocalePreference(Locale.FromSystem()));

    public static void Attach(ITextCatalog catalog, ILocalePreference preference) => Presenter.Replace(catalog, preference);
    public static string T(string key, params object[] args) => Presenter.Get(key, args);

    private ITextCatalog _catalog;
    private ILocalePreference _preference;
    public I18n(ITextCatalog catalog, ILocalePreference preference)
    {
        _catalog = catalog;
        _preference = preference;
        preference.Current.ApplyCulture();
        preference.Changed += OnPreferenceChanged;
        catalog.Changed += Notify;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Locale Locale => _preference.Current;
    public int Revision { get; private set; }
    public string this[string key] => Get(key);
    public string Get(string key) => _catalog.Get(_preference.Current, key);
    public string Get(string key, params object[] args) =>
        args.Length == 0 ? Get(key) : _catalog.Get(_preference.Current, key, args);
    public string Error(Exception exception) => exception switch
    {
        TaskCanceledException => Get(TextKey.RequestTimedOut),
        OperationCanceledException => Get(TextKey.Cancelled),
        TimeoutException => Get(TextKey.RequestTimedOut),
        HttpRequestException => Get(TextKey.ServerUnreachable),
        System.Net.Sockets.SocketException => Get(TextKey.ServerUnreachable),
        ChatApiException { Code: "forbidden" } => Get(TextKey.ActionNotAllowed),
        ChatApiException { Code: "not_found" or "invalid_reply" or "invalid_channel" } => Get(TextKey.ContentUnavailable),
        ChatApiException { Code: "unauthorized" or "invalid_credentials" } => Get(TextKey.SignInAgain),
        LanguagePackException pack => Get(pack.Key),
        ClientFault fault => Get(fault.Key, fault.Args),
        _ => exception.Message
    };
    public void Dispose()
    {
        _preference.Changed -= OnPreferenceChanged;
        _catalog.Changed -= Notify;
    }

    private void Replace(ITextCatalog catalog, ILocalePreference preference)
    {
        _preference.Changed -= OnPreferenceChanged;
        _catalog.Changed -= Notify;
        _catalog = catalog;
        _preference = preference;
        preference.Current.ApplyCulture();
        preference.Changed += OnPreferenceChanged;
        catalog.Changed += Notify;
        Notify();
    }

    private void OnPreferenceChanged(Locale locale)
    {
        locale.ApplyCulture();
        Notify();
    }

    private void Notify()
    {
        Revision++;
        PropertyChanged?.Invoke(this, new(nameof(Revision)));
        PropertyChanged?.Invoke(this, new(string.Empty));
        PropertyChanged?.Invoke(this, new("Item[]"));
    }
}
