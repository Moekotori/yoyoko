using System.Collections.ObjectModel;
using Chat.Core.Sessions;
using Chat.Core.Voice;
using Chat.UI.Appearance;
using Chat.UI.Auth;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Shortcuts;
using Chat.UI.Voice;

namespace Chat.UI.Settings;

public interface IChatChrome
{
    bool EnterToSend { get; }
    bool Compact { get; }
    bool ReduceMotion { get; }
    bool UltraLightEnabled { get; }
    void SetEnterToSend(bool value);
    void SetCompact(bool value);
    void SetReduceMotion(bool value);
    void SetUltraLightEnabled(bool value);
    event Action? Changed;
}

public sealed class LanguageOption(Locale locale, bool selected, bool custom) : ObservableObject
{
    private bool _isSelected = selected;
    public Locale Locale { get; } = locale;
    public string NativeName => Locale.NativeName;
    public bool IsCustom { get; } = custom;
    public bool IsSelected
    {
        get => _isSelected;
        internal set { if (_isSelected == value) return; _isSelected = value; Changed(); }
    }
}

public enum SettingsSection { General, Appearance, Keyboard, Voice, Profile, Connection }

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ILocalePreference _preference;
    private readonly IChatChrome _chrome;
    private readonly IAppearancePreference _appearance;
    private readonly ISystemTransportPreference _transport;
    private readonly I18n _text;
    private readonly ILanguagePacks _packs;
    private readonly Action<Exception> _onError;
    private SettingsSection _section;
    private string _languagePackNotice = "";
    private bool _languagePackDropActive;
    private bool _showLanguagePacks;
    public SettingsViewModel(ILocalePreference preference, IChatChrome chrome, IAppearancePreference appearance,
        IShortcutPreference shortcuts, WallpaperSession wallpaper, Action close, Func<InstanceSession?> session,
        Func<Task<PickedFile?>> pickAvatar, Action<Exception> onError, I18n text, VoiceDevicesViewModel devices, ConnectionSettingsViewModel connection, AuthFormViewModel auth, ILanguagePacks packs,
        ISystemTransportPreference transport, IVoiceQualityHost quality)
    {
        _preference = preference;
        _chrome = chrome;
        _appearance = appearance;
        _transport = transport;
        _text = text;
        _packs = packs;
        _onError = onError;
        Quality = quality;
        Wallpaper = wallpaper;
        Shortcuts = new(shortcuts, chrome, text);
        Profile = new(session, pickAvatar, onError, text);
        Devices = devices;
        Connection = connection;
        Auth = auth;
        ColorSchemes.Add(new(ColorScheme.Dark));
        ColorSchemes.Add(new(ColorScheme.Light));
        Close = new(_ => { Shortcuts.CancelCapture(); close(); });
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
        ToggleUltraLight = new(_ => _chrome.SetUltraLightEnabled(!_chrome.UltraLightEnabled));
        ToggleLanguagePacks = new(_ => ShowLanguagePacks = !ShowLanguagePacks);
        ToggleHeadsetMediaKeys = new(_ => _transport.SetHeadsetMediaKeys(!_transport.HeadsetMediaKeys));
        ToggleLoopback = new(devices.ToggleLoopbackAsync, onError);
        ImportLanguagePack = new(ImportLanguagePackAsync, onError);
        ExportLanguageTemplate = new(ExportLanguageTemplateAsync, onError);
        RemoveLanguagePack = new(_ => RemoveCurrentPack());
        devices.PropertyChanged += (_, _) =>
        {
            Changed(nameof(LoopbackLabel));
            Changed(nameof(LoopbackActive));
        };
        _preference.Changed += OnChanged;
        _chrome.Changed += OnChrome;
        _appearance.Changed += OnAppearance;
        _transport.Changed += OnTransport;
        _packs.Changed += OnPacksChanged;
        Refresh();
        NotifyChrome();
        NotifyAppearance();
        Changed(nameof(HeadsetMediaKeys));
    }
    public ActionCommand Close { get; }
    public ActionCommand Navigate { get; }
    public SettingsSection Section
    {
        get => _section;
        internal set
        {
            if (_section == value) return;
            if (value != SettingsSection.Keyboard) Shortcuts.CancelCapture();
            _section = value;
            Changed();
            Changed(nameof(ShowGeneral));
            Changed(nameof(ShowAppearance));
            Changed(nameof(ShowKeyboard));
            Changed(nameof(ShowVoice));
            Changed(nameof(ShowProfile));
            Changed(nameof(ShowConnection));
            Changed(nameof(SectionTitle));
            if (value == SettingsSection.Voice) _ = Devices.RefreshAsync();
            Connection.SetWatching(value == SettingsSection.Connection);
        }
    }
    public ConnectionSettingsViewModel Connection { get; }
    public AuthFormViewModel Auth { get; }
    public bool ShowConnection => Section == SettingsSection.Connection;
    public bool ShowGeneral => Section == SettingsSection.General;
    public bool ShowAppearance => Section == SettingsSection.Appearance;
    public bool ShowKeyboard => Section == SettingsSection.Keyboard;
    public bool ShowVoice => Section == SettingsSection.Voice;
    public bool ShowProfile => Section == SettingsSection.Profile;
    public string SectionTitle => _text.Get(Section switch
    {
        SettingsSection.Appearance => TextKey.Appearance,
        SettingsSection.Keyboard => TextKey.KeyboardShortcuts,
        SettingsSection.Voice => TextKey.Voice,
        SettingsSection.Profile => TextKey.Profile,
        SettingsSection.Connection => TextKey.ServerConnection,
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
    public ActionCommand ToggleUltraLight { get; }
    public ActionCommand ToggleLanguagePacks { get; }
    public ActionCommand ToggleHeadsetMediaKeys { get; }
    public AsyncCommand ToggleLoopback { get; }
    public AsyncCommand ImportLanguagePack { get; }
    public AsyncCommand ExportLanguageTemplate { get; }
    public ActionCommand RemoveLanguagePack { get; }
    public Func<Task<IReadOnlyList<string>>>? PickLanguagePackPaths { get; set; }
    public Func<string, string, Task>? SaveLanguageTemplate { get; set; }
    public bool CanRemoveLanguagePack => _packs.HasOverlay(_preference.Current.Code);
    public bool ShowLanguagePacks
    {
        get => _showLanguagePacks;
        private set { if (_showLanguagePacks == value) return; _showLanguagePacks = value; Changed(); }
    }
    public bool LanguagePackDropActive
    {
        get => _languagePackDropActive;
        set
        {
            if (_languagePackDropActive == value) return;
            _languagePackDropActive = value;
            if (value) ShowLanguagePacks = true;
            Changed();
        }
    }
    public string LanguagePackNotice
    {
        get => _languagePackNotice;
        private set { if (_languagePackNotice == value) return; _languagePackNotice = value; Changed(); Changed(nameof(HasLanguagePackNotice)); }
    }
    public bool HasLanguagePackNotice => _languagePackNotice.Length > 0;
    public bool LoopbackActive => Devices.LoopbackActive;
    public string LoopbackLabel => Devices.LoopbackLabel;
    public ProfileViewModel Profile { get; }
    public VoiceDevicesViewModel Devices { get; }
    public IVoiceQualityHost Quality { get; }
    public WallpaperSession Wallpaper { get; }
    public ShortcutSettingsViewModel Shortcuts { get; }
    public ObservableCollection<ColorSchemeChoice> ColorSchemes { get; } = [];
    public ColorSchemeChoice? SelectedColorScheme
    {
        get => ColorSchemes.FirstOrDefault(item => item.Scheme == _appearance.ColorScheme);
        set { if (value is not null && value.Scheme != _appearance.ColorScheme) _appearance.SetColorScheme(value.Scheme); }
    }
    public ObservableCollection<LanguageOption> Languages { get; } = [];
    public LanguageOption? SelectedLanguage
    {
        get => Languages.FirstOrDefault(option => option.Locale == _preference.Current);
        set { if (value is not null && value.Locale != _preference.Current) _preference.Set(value.Locale); }
    }
    public IReadOnlyList<string> SendShortcutChoices =>
    [
        _text.Get(TextKey.EnterToSend),
        _text.Get(TextKey.CtrlEnterToSend)
    ];
    public int SendMode
    {
        get => _chrome.EnterToSend ? 0 : 1;
        set { if (value is 0 or 1 && value != SendMode) _chrome.SetEnterToSend(value == 0); }
    }
    public bool UltraLightEnabled { get => _chrome.UltraLightEnabled; set { if (value != UltraLightEnabled) _chrome.SetUltraLightEnabled(value); } }
    public bool HeadsetMediaKeys
    {
        get => _transport.HeadsetMediaKeys;
        set { if (value != HeadsetMediaKeys) _transport.SetHeadsetMediaKeys(value); }
    }
    public bool EnterToSend => _chrome.EnterToSend;
    public bool Compact { get => _chrome.Compact; set { if (value != Compact) _chrome.SetCompact(value); } }
    public bool ReduceMotion { get => _chrome.ReduceMotion; set { if (value != ReduceMotion) _chrome.SetReduceMotion(value); } }
    public void Dispose()
    {
        _preference.Changed -= OnChanged;
        _chrome.Changed -= OnChrome;
        _appearance.Changed -= OnAppearance;
        _transport.Changed -= OnTransport;
        _packs.Changed -= OnPacksChanged;
        Connection.Dispose();
        Shortcuts.Dispose();
    }
    private void OnChanged(Locale _) => Refresh();
    private void OnChrome() => NotifyChrome();
    private void OnAppearance() => NotifyAppearance();
    private void OnTransport() => Changed(nameof(HeadsetMediaKeys));
    private void NotifyChrome()
    {
        Changed(nameof(EnterToSend));
        Changed(nameof(SendMode));
        Changed(nameof(Compact));
        Changed(nameof(ReduceMotion));
        Changed(nameof(UltraLightEnabled));
    }
    private void SyncLanguages()
    {
        var available = _packs.Available;
        var selected = _preference.Current;
        var same = Languages.Count == available.Count;
        if (same)
            for (var i = 0; i < available.Count; i++)
                if (!Languages[i].Locale.Code.Equals(available[i].Code, StringComparison.OrdinalIgnoreCase))
                {
                    same = false;
                    break;
                }
        if (!same)
        {
            Languages.Clear();
            foreach (var locale in available)
                Languages.Add(new(locale, locale.Code.Equals(selected.Code, StringComparison.OrdinalIgnoreCase),
                    _packs.HasOverlay(locale.Code) && !Locale.IsBuiltIn(locale.Code)));
        }
        else
            foreach (var option in Languages)
                option.IsSelected = option.Locale.Code.Equals(selected.Code, StringComparison.OrdinalIgnoreCase);
        Changed(nameof(SelectedLanguage));
        Changed(nameof(CanRemoveLanguagePack));
    }
    private void OnPacksChanged() => Refresh();
    public void ImportDropped(IEnumerable<string> paths)
    {
        ShowLanguagePacks = true;
        try
        {
            Locale? last = null;
            foreach (var path in paths)
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    last = ImportPath(path);
            if (last is { } locale && !Locale.IsBuiltIn(locale.Code))
                _preference.Set(locale);
        }
        catch (Exception exception) { LanguagePackNotice = _text.Error(exception); _onError(exception); }
    }
    private async Task ImportLanguagePackAsync()
    {
        if (PickLanguagePackPaths is null) return;
        var paths = await PickLanguagePackPaths();
        ImportDropped(paths);
    }
    private async Task ExportLanguageTemplateAsync()
    {
        if (SaveLanguageTemplate is null) return;
        await SaveLanguageTemplate("language-pack.template.json", _packs.TemplateJson(new TextCatalog()));
    }
    private Locale ImportPath(string path)
    {
        var locale = _packs.ImportFile(path);
        var count = _packs.Find(locale.Code)?.Strings.Count ?? 0;
        LanguagePackNotice = _text.Get(TextKey.LanguagePackImported, locale.NativeName, count);
        return locale;
    }
    private void RemoveCurrentPack()
    {
        var code = _preference.Current.Code;
        if (!_packs.HasOverlay(code)) return;
        var extra = !Locale.IsBuiltIn(code);
        _packs.Remove(code);
        if (extra) _preference.Set(Locale.English);
        LanguagePackNotice = "";
    }
    private void Refresh()
    {
        SyncLanguages();
        Changed(nameof(SectionTitle));
        Changed(nameof(SendShortcutChoices));
        Profile.NotifyText();
        foreach (var option in ColorSchemes)
            option.Name = _text.Get(option.Scheme == ColorScheme.Light ? TextKey.ColorSchemeLight : TextKey.ColorSchemeDark);
        Changed(nameof(SelectedColorScheme));
    }
    private void NotifyAppearance()
    {
        Changed(nameof(SelectedColorScheme));
    }
}
