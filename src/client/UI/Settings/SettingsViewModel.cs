using System.Collections.ObjectModel;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Settings;

public interface IChatChrome
{
    bool EnterToSend { get; }
    bool Compact { get; }
    bool ReduceMotion { get; }
    void SetEnterToSend(bool value);
    void SetCompact(bool value);
    void SetReduceMotion(bool value);
    event Action? Changed;
}

public sealed class LanguageOption(Locale locale, bool selected)
{
    public Locale Locale { get; } = locale;
    public string NativeName => Locale.NativeName;
    public bool IsSelected { get; } = selected;
}

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILocalePreference _preference;
    private readonly IChatChrome _chrome;
    public SettingsViewModel(ILocalePreference preference, IChatChrome chrome, Action close, Func<InstanceSession?> session,
        Func<Task<PickedFile?>> pickAvatar, Action<Exception> onError, I18n text)
    {
        _preference = preference;
        _chrome = chrome;
        Profile = new(session, pickAvatar, onError, text);
        Close = new(_ => close());
        Select = new(value =>
        {
            if (value is LanguageOption option) _preference.Set(option.Locale);
            else if (value is Locale locale) _preference.Set(locale);
        });
        ToggleEnterToSend = new(_ => _chrome.SetEnterToSend(!_chrome.EnterToSend));
        ToggleCompact = new(_ => _chrome.SetCompact(!_chrome.Compact));
        ToggleReduceMotion = new(_ => _chrome.SetReduceMotion(!_chrome.ReduceMotion));
        _preference.Changed += OnChanged;
        _chrome.Changed += OnChrome;
        Refresh();
        NotifyChrome();
    }
    public ActionCommand Close { get; }
    public ActionCommand Select { get; }
    public ActionCommand ToggleEnterToSend { get; }
    public ActionCommand ToggleCompact { get; }
    public ActionCommand ToggleReduceMotion { get; }
    public ProfileViewModel Profile { get; }
    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public bool EnterToSend => _chrome.EnterToSend;
    public bool Compact => _chrome.Compact;
    public bool ReduceMotion => _chrome.ReduceMotion;
    public void Dispose()
    {
        _preference.Changed -= OnChanged;
        _chrome.Changed -= OnChrome;
    }
    private void OnChanged(Locale _) => Refresh();
    private void OnChrome() => NotifyChrome();
    private void NotifyChrome()
    {
        Changed(nameof(EnterToSend));
        Changed(nameof(Compact));
        Changed(nameof(ReduceMotion));
    }
    private void Refresh()
    {
        Languages.Clear();
        foreach (var locale in Locale.Supported)
            Languages.Add(new(locale, locale == _preference.Current));
    }
}
