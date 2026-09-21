using System.Text.Json;
using System.Text.Json.Serialization;
using Chat.Core.Voice;
using Chat.Localization;
using Chat.UI.Appearance;
using Chat.UI.Settings;
using Chat.UI.Shortcuts;

namespace Chat.App;

internal sealed class FileLocalePreference : ILocalePreference, IChatChrome, IVoiceDevicePreference, IAppearancePreference, IShortcutPreference
{
    private readonly string _path;
    private Locale _current;
    private bool _enterToSend = true;
    private bool _compact;
    private bool _reduceMotion;
    private bool _ultraLightEnabled;
    private string? _inputDevice;
    private string? _outputDevice;
    private ColorScheme _colorScheme = ColorScheme.Dark;
    private bool _wallpaperEnabled;
    private string _wallpaperFile = "";
    private string _wallpaperLabel = "";
    private int _wallpaperBlur;
    private int _wallpaperBrightness = 60;
    private int _persistGen;
    private readonly Dictionary<string, string> _shortcuts;
    private FileLocalePreference(string path, PreferenceFile stored, Locale locale)
    {
        _path = path;
        _current = locale;
        _enterToSend = stored.EnterToSend;
        _compact = stored.Compact;
        _reduceMotion = stored.ReduceMotion;
        _ultraLightEnabled = stored.UltraLightEnabled;
        _inputDevice = EmptyToNull(stored.InputDevice);
        _outputDevice = EmptyToNull(stored.OutputDevice);
        _colorScheme = stored.ColorScheme.Equals("light", StringComparison.OrdinalIgnoreCase) ? ColorScheme.Light : ColorScheme.Dark;
        _wallpaperEnabled = stored.WallpaperEnabled;
        _wallpaperFile = stored.WallpaperFile ?? "";
        _wallpaperLabel = stored.WallpaperLabel ?? "";
        _wallpaperBlur = Math.Clamp(stored.WallpaperBlur, 0, 100);
        _wallpaperBrightness = Math.Clamp(stored.WallpaperBrightness, 0, 100);
        _shortcuts = stored.Shortcuts is { Count: > 0 }
            ? new Dictionary<string, string>(stored.Shortcuts, StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
        locale.ApplyCulture();
    }

    public static FileLocalePreference Load(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var stored = Read(path) ?? new PreferenceFile();
        var env = Environment.GetEnvironmentVariable("CHAT_LOCALE");
        var locale = stored.Locale is { Length: > 0 } ? Locale.Parse(stored.Locale)
            : env is { Length: > 0 } ? Locale.Parse(env)
            : Locale.FromSystem();
        return new(path, stored, locale);
    }

    public Locale Current => _current;
    public bool EnterToSend => _enterToSend;
    public bool Compact => _compact;
    public bool ReduceMotion => _reduceMotion;
    public bool UltraLightEnabled => _ultraLightEnabled;
    public string? InputDeviceId => _inputDevice;
    public string? OutputDeviceId => _outputDevice;
    public ColorScheme ColorScheme => _colorScheme;
    public bool WallpaperEnabled => _wallpaperEnabled;
    public string WallpaperFile => _wallpaperFile;
    public string WallpaperLabel => _wallpaperLabel;
    public string WallpaperDirectory => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_path))!, "wallpaper");
    public int WallpaperBlur => _wallpaperBlur;
    public int WallpaperBrightness => _wallpaperBrightness;
    public IReadOnlyDictionary<string, string> Overrides => _shortcuts;
    public event Action<Locale>? Changed;
    public event Action? ShortcutChanged;
    event Action? IShortcutPreference.Changed
    {
        add => ShortcutChanged += value;
        remove => ShortcutChanged -= value;
    }
    event Action? IChatChrome.Changed
    {
        add => ChromeChanged += value;
        remove => ChromeChanged -= value;
    }
    event Action? IAppearancePreference.Changed
    {
        add => AppearanceChanged += value;
        remove => AppearanceChanged -= value;
    }
    private event Action? ChromeChanged;
    private event Action? AppearanceChanged;

    public void Set(Locale locale)
    {
        if (locale == _current) return;
        _current = locale;
        locale.ApplyCulture();
        Persist();
        Changed?.Invoke(locale);
    }

    public void SetEnterToSend(bool value)
    {
        if (_enterToSend == value) return;
        _enterToSend = value;
        Persist();
        ChromeChanged?.Invoke();
    }

    public void SetCompact(bool value)
    {
        if (_compact == value) return;
        _compact = value;
        Persist();
        ChromeChanged?.Invoke();
    }

    public void SetReduceMotion(bool value)
    {
        if (_reduceMotion == value) return;
        _reduceMotion = value;
        Persist();
        ChromeChanged?.Invoke();
    }

    public void SetUltraLightEnabled(bool value)
    {
        if (_ultraLightEnabled == value) return;
        _ultraLightEnabled = value;
        Persist();
        ChromeChanged?.Invoke();
    }

    public void SetDevices(string? inputId, string? outputId)
    {
        inputId = EmptyToNull(inputId);
        outputId = EmptyToNull(outputId);
        if (_inputDevice == inputId && _outputDevice == outputId) return;
        _inputDevice = inputId;
        _outputDevice = outputId;
        Persist();
    }

    public void SetColorScheme(ColorScheme value)
    {
        if (_colorScheme == value) return;
        _colorScheme = value;
        Persist();
        AppearanceChanged?.Invoke();
    }

    public void SetWallpaperEnabled(bool value)
    {
        if (_wallpaperEnabled == value) return;
        _wallpaperEnabled = value;
        Persist();
        AppearanceChanged?.Invoke();
    }

    public void SetWallpaperFile(string file, string label)
    {
        file = string.IsNullOrWhiteSpace(file) ? "" : file;
        label = string.IsNullOrWhiteSpace(label) ? "" : label;
        if (_wallpaperFile == file && _wallpaperLabel == label) return;
        _wallpaperFile = file;
        _wallpaperLabel = label;
        Persist();
        AppearanceChanged?.Invoke();
    }

    public void SetWallpaperBlur(int value)
    {
        value = Math.Clamp(value, 0, 100);
        if (_wallpaperBlur == value) return;
        _wallpaperBlur = value;
        AppearanceChanged?.Invoke();
        PersistSoon();
    }

    public void SetWallpaperBrightness(int value)
    {
        value = Math.Clamp(value, 0, 100);
        if (_wallpaperBrightness == value) return;
        _wallpaperBrightness = value;
        AppearanceChanged?.Invoke();
        PersistSoon();
    }

    public void Assign(string action, string? chord)
    {
        if (string.IsNullOrWhiteSpace(action)) return;
        if (chord is null)
        {
            if (!_shortcuts.Remove(action)) return;
        }
        else if (_shortcuts.TryGetValue(action, out var current) && current == chord) return;
        else _shortcuts[action] = chord;
        Persist();
        ShortcutChanged?.Invoke();
    }

    public void Reset()
    {
        if (_shortcuts.Count == 0) return;
        _shortcuts.Clear();
        Persist();
        ShortcutChanged?.Invoke();
    }

    public void Flush() => Persist();

    private async void PersistSoon()
    {
        var gen = ++_persistGen;
        try { await Task.Delay(180); }
        catch (TaskCanceledException) { return; }
        if (gen == _persistGen) Persist();
    }

    private void Persist()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(
            new PreferenceFile(_current.Code, _enterToSend, _compact, _reduceMotion, _inputDevice, _outputDevice,
                _colorScheme == ColorScheme.Light ? "light" : "dark", _wallpaperEnabled, _wallpaperFile, _wallpaperLabel,
                _wallpaperBlur, _wallpaperBrightness,
                _shortcuts.Count == 0 ? null : new Dictionary<string, string>(_shortcuts), _ultraLightEnabled),
            PreferenceJson.Default.PreferenceFile));
        File.Move(temp, _path, true);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static PreferenceFile? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), PreferenceJson.Default.PreferenceFile);
        }
        catch (JsonException) { return null; }
    }
}

internal sealed record PreferenceFile(string Locale = "", bool EnterToSend = true, bool Compact = false, bool ReduceMotion = false,
    string? InputDevice = null, string? OutputDevice = null, string ColorScheme = "dark", bool WallpaperEnabled = false,
    string WallpaperFile = "", string WallpaperLabel = "", int WallpaperBlur = 0, int WallpaperBrightness = 60,
    Dictionary<string, string>? Shortcuts = null, bool UltraLightEnabled = false);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PreferenceFile))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class PreferenceJson : JsonSerializerContext;
