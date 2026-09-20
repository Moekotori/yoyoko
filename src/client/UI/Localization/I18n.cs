using System.ComponentModel;
using Chat.Core;
using Chat.Localization;

namespace Chat.UI.Localization;

public sealed class I18n : INotifyPropertyChanged, IDisposable
{
    public static I18n Presenter { get; private set; } = new(new TextCatalog(), new MemoryLocalePreference(Locale.English));
    public static void Use(I18n instance) => Presenter = instance;

    private readonly ITextCatalog _catalog;
    private readonly ILocalePreference _preference;
    public I18n(ITextCatalog catalog, ILocalePreference preference)
    {
        _catalog = catalog;
        _preference = preference;
        preference.Current.ApplyCulture();
        preference.Changed += OnPreferenceChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public Locale Locale => _preference.Current;
    public string this[string key] => Get(key);
    public string Get(string key, params object[] args) => _catalog.Get(_preference.Current, key, args);
    public string Error(Exception exception) => exception switch
    {
        OperationCanceledException => Get(TextKey.Cancelled),
        ClientFault fault => Get(fault.Key, fault.Args),
        _ => exception.Message
    };
    public void Dispose() => _preference.Changed -= OnPreferenceChanged;
    private void OnPreferenceChanged(Locale locale)
    {
        locale.ApplyCulture();
        PropertyChanged?.Invoke(this, new(string.Empty));
        PropertyChanged?.Invoke(this, new("Item[]"));
    }
}
