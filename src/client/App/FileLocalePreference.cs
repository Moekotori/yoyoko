using System.Text.Json;
using System.Text.Json.Serialization;
using Chat.Localization;
using Chat.UI.Settings;

namespace Chat.App;

internal sealed class FileLocalePreference : ILocalePreference, IChatChrome
{
    private readonly string _path;
    private Locale _current;
    private bool _enterToSend = true;
    private bool _compact;
    private bool _reduceMotion;
    private FileLocalePreference(string path, PreferenceFile stored, Locale locale)
    {
        _path = path;
        _current = locale;
        _enterToSend = stored.EnterToSend;
        _compact = stored.Compact;
        _reduceMotion = stored.ReduceMotion;
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
    public event Action<Locale>? Changed;
    event Action? IChatChrome.Changed
    {
        add => ChromeChanged += value;
        remove => ChromeChanged -= value;
    }
    private event Action? ChromeChanged;

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

    private void Persist()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(
            new PreferenceFile(_current.Code, _enterToSend, _compact, _reduceMotion),
            PreferenceJson.Default.PreferenceFile));
        File.Move(temp, _path, true);
    }

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

internal sealed record PreferenceFile(string Locale = "", bool EnterToSend = true, bool Compact = false, bool ReduceMotion = false);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PreferenceFile))]
internal partial class PreferenceJson : JsonSerializerContext;
