using System.Collections.ObjectModel;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Voice;

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

public sealed class LanguageOption(Locale locale, bool selected) : ObservableObject
{
    private bool _isSelected = selected;
    public Locale Locale { get; } = locale;
    public string NativeName => Locale.NativeName;
    public bool IsSelected
    {
        get => _isSelected;
        internal set { if (_isSelected == value) return; _isSelected = value; Changed(); }
    }
}

public enum SettingsSection { General, Appearance, Voice, Profile }

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILocalePreference _preference;
    private readonly IChatChrome _chrome;
    private readonly I18n _text;
    private SettingsSection _section;
    public SettingsViewModel(ILocalePreference preference, IChatChrome chrome, Action close, Func<InstanceSession?> session,
        Func<Task<PickedFile?>> pickAvatar, Action<Exception> onError, I18n text, VoiceDevicesViewModel devices)
    {
        _preference = preference;
        _chrome = chrome;
        _text = text;
        Profile = new(session, pickAvatar, onError, text);
        Devices = devices;
        Close = new(_ => close());
        Navigate = new(value => { if (value is SettingsSection section) Section = section; });
        Select = new(value =>
        {
            if (value is LanguageOption option) _preference.Set(option.Locale);
            else if (value is Locale locale) _preference.Set(locale);
        });
        ToggleEnterToSend = new(_ => _chrome.SetEnterToSend(!_chrome.EnterToSend));
        UseEnterToSend = new(_ => _chrome.SetEnterToSend(true));
        UseModifiedEnterToSend = new(_ => _chrome.SetEnterToSend(false));
        UseComfortableLayout = new(_ => _chrome.SetCompact(false));
        UseCompactLayout = new(_ => _chrome.SetCompact(true));
        ToggleCompact = new(_ => _chrome.SetCompact(!_chrome.Compact));
        ToggleReduceMotion = new(_ => _chrome.SetReduceMotion(!_chrome.ReduceMotion));
        _preference.Changed += OnChanged;
        _chrome.Changed += OnChrome;
        Refresh();
        NotifyChrome();
    }
    public ActionCommand Close { get; }
    public ActionCommand Navigate { get; }
    public SettingsSection Section
    {
        get => _section;
        private set
        {
            if (_section == value) return;
            _section = value;
            Changed();
            Changed(nameof(ShowGeneral));
            Changed(nameof(ShowAppearance));
            Changed(nameof(ShowVoice));
            Changed(nameof(ShowProfile));
            Changed(nameof(SectionTitle));
            if (value == SettingsSection.Voice) _ = Devices.RefreshAsync();
        }
    }
    public bool ShowGeneral => Section == SettingsSection.General;
    public bool ShowAppearance => Section == SettingsSection.Appearance;
    public bool ShowVoice => Section == SettingsSection.Voice;
    public bool ShowProfile => Section == SettingsSection.Profile;
    public string SectionTitle => _text.Get(Section switch
    {
        SettingsSection.Appearance => TextKey.Appearance,
        SettingsSection.Voice => TextKey.Voice,
        SettingsSection.Profile => TextKey.Profile,
        _ => TextKey.GeneralSettings
    });
    public ActionCommand Select { get; }
    public ActionCommand ToggleEnterToSend { get; }
    public ActionCommand UseEnterToSend { get; }
    public ActionCommand UseModifiedEnterToSend { get; }
    public ActionCommand UseComfortableLayout { get; }
    public ActionCommand UseCompactLayout { get; }
    public string ModifierKey => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";
    public ActionCommand ToggleCompact { get; }
    public ActionCommand ToggleReduceMotion { get; }
    public ProfileViewModel Profile { get; }
    public VoiceDevicesViewModel Devices { get; }
    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public LanguageOption? SelectedLanguage
    {
        get => Languages.FirstOrDefault(option => option.Locale == _preference.Current);
        set { if (value is not null && value.Locale != _preference.Current) _preference.Set(value.Locale); }
    }
    public int SendMode
    {
        get => _chrome.EnterToSend ? 0 : 1;
        set { if (value is 0 or 1 && value != SendMode) _chrome.SetEnterToSend(value == 0); }
    }
    public bool EnterToSend => _chrome.EnterToSend;
    public bool Compact { get => _chrome.Compact; set { if (value != Compact) _chrome.SetCompact(value); } }
    public bool ReduceMotion { get => _chrome.ReduceMotion; set { if (value != ReduceMotion) _chrome.SetReduceMotion(value); } }
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
        Changed(nameof(SendMode));
        Changed(nameof(Compact));
        Changed(nameof(ReduceMotion));
    }
    private void Refresh()
    {
        if (Languages.Count == 0)
            foreach (var locale in Locale.Supported)
                Languages.Add(new(locale, locale == _preference.Current));
        else
            foreach (var option in Languages)
                option.IsSelected = option.Locale == _preference.Current;
        Changed(nameof(SelectedLanguage));
        Changed(nameof(SectionTitle));
    }
}
