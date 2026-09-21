using System.ComponentModel;
using Chat.Core;
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
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Locale Locale => _preference.Current;
    public int Revision { get; private set; }
    public string this[string key] => Get(key);
    public string Get(string key, params object[] args) => _catalog.Get(_preference.Current, key, args);
    public string Error(Exception exception) => exception switch
    {
        OperationCanceledException => Get(TextKey.Cancelled),
        ClientFault fault => Get(fault.Key, fault.Args),
        _ => exception.Message
    };
    public void Dispose() => _preference.Changed -= OnPreferenceChanged;

    private void Replace(ITextCatalog catalog, ILocalePreference preference)
    {
        _preference.Changed -= OnPreferenceChanged;
        _catalog = catalog;
        _preference = preference;
        preference.Current.ApplyCulture();
        preference.Changed += OnPreferenceChanged;
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
