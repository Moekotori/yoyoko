using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Chat.Localization;

public sealed class LanguagePackException(string key) : IOException(key)
{
    public string Key { get; } = key;
}

public sealed class LoadedLanguagePack
{
    public LoadedLanguagePack(Locale locale, string? fallback, FrozenDictionary<string, string> strings)
    {
        Locale = locale;
        Fallback = fallback;
        Strings = strings;
    }
    public Locale Locale { get; }
    public string? Fallback { get; }
    public FrozenDictionary<string, string> Strings { get; }
}

public interface ILanguagePacks
{
    IReadOnlyList<Locale> Available { get; }
    bool HasOverlay(string code);
    LoadedLanguagePack? Find(string code);
    Locale ImportFile(string path);
    Locale ImportBytes(ReadOnlySpan<byte> utf8);
    void Remove(string code);
    string TemplateJson(ITextCatalog catalog);
    event Action? Changed;
}

public sealed class LanguagePackStore : ILanguagePacks
{
    public const int MaxBytes = 256 * 1024;
    public const int MaxPacks = 32;
    public const int MaxNameLength = 40;
    public const int MaxStringLength = 512;
    private static readonly Regex CodePattern = new("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly string _directory;
    private readonly Dictionary<string, LoadedLanguagePack> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private LanguagePackStore(string directory) => _directory = directory;

    public static LanguagePackStore Open(string directory)
    {
        Directory.CreateDirectory(directory);
        var store = new LanguagePackStore(directory);
        store.Reload();
        return store;
    }

    public IReadOnlyList<Locale> Available
    {
        get
        {
            if (_loaded.Count == 0) return Locale.Supported;
            var extra = new List<Locale>(Locale.Supported.Count + _loaded.Count);
            extra.AddRange(Locale.Supported);
            foreach (var pack in _loaded.Values)
                if (!Locale.IsBuiltIn(pack.Locale.Code)) extra.Add(pack.Locale);
            return extra;
        }
    }

    public event Action? Changed;
    public bool HasOverlay(string code) => _loaded.ContainsKey(code);
    public LoadedLanguagePack? Find(string code) => _loaded.TryGetValue(code, out var pack) ? pack : null;

    public Locale ImportFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new LanguagePackException(TextKey.LanguagePackInvalid);
        if (info.Length is <= 0 or > MaxBytes) throw new LanguagePackException(TextKey.LanguagePackTooLarge);
        return ImportBytes(File.ReadAllBytes(path));
    }

    public Locale ImportBytes(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length is 0 or > MaxBytes) throw new LanguagePackException(TextKey.LanguagePackTooLarge);
        var loaded = Parse(utf8);
        if (_loaded.Count >= MaxPacks && !_loaded.ContainsKey(loaded.Locale.Code))
            throw new LanguagePackException(TextKey.LanguagePackInvalid);
        var dest = Path.Combine(_directory, loaded.Locale.Code.ToLowerInvariant() + ".json");
        var temp = dest + ".tmp";
        File.WriteAllBytes(temp, utf8.ToArray());
        File.Move(temp, dest, true);
        _loaded[loaded.Locale.Code] = loaded;
        Changed?.Invoke();
        return loaded.Locale;
    }

    public void Remove(string code)
    {
        if (!_loaded.Remove(code)) return;
        var path = Path.Combine(_directory, code.ToLowerInvariant() + ".json");
        if (File.Exists(path)) File.Delete(path);
        Changed?.Invoke();
    }

    public string TemplateJson(ITextCatalog catalog)
    {
        var strings = new Dictionary<string, string>(catalog.Keys.Count, StringComparer.Ordinal);
        foreach (var key in catalog.Keys.OrderBy(key => key, StringComparer.Ordinal))
            strings[key] = catalog.Get(Locale.English, key);
        return JsonSerializer.Serialize(new LanguagePackFile("xx", "Language name", "en", strings), PackJson.Default.LanguagePackFile);
    }

    private void Reload()
    {
        _loaded.Clear();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            if (_loaded.Count >= MaxPacks) break;
            try
            {
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length is 0 or > MaxBytes) continue;
                var pack = Parse(bytes);
                _loaded[pack.Locale.Code] = pack;
            }
            catch (Exception) { /* skip broken packs */ }
        }
    }

    internal static LoadedLanguagePack Parse(ReadOnlySpan<byte> utf8)
    {
        LanguagePackFile? file;
        try { file = JsonSerializer.Deserialize(utf8, PackJson.Default.LanguagePackFile); }
        catch (JsonException) { throw new LanguagePackException(TextKey.LanguagePackInvalid); }
        if (file is null || file.Strings is null || file.Strings.Count == 0)
            throw new LanguagePackException(TextKey.LanguagePackInvalid);
        var code = (file.Code ?? "").Trim();
        var name = (file.Name ?? "").Trim();
        if (code.Length is 0 or > 16 || !CodePattern.IsMatch(code) || name.Length is 0 or > MaxNameLength)
            throw new LanguagePackException(TextKey.LanguagePackInvalid);
        foreach (var ch in name)
            if (char.IsControl(ch)) throw new LanguagePackException(TextKey.LanguagePackInvalid);
        string? fallback = string.IsNullOrWhiteSpace(file.Fallback) ? null : file.Fallback.Trim();
        if (fallback is not null && !Locale.IsBuiltIn(fallback))
            throw new LanguagePackException(TextKey.LanguagePackInvalid);
        if (file.Strings.Count > 512) throw new LanguagePackException(TextKey.LanguagePackInvalid);
        var strings = new Dictionary<string, string>(file.Strings.Count, StringComparer.Ordinal);
        foreach (var (key, value) in file.Strings)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 80 || value is null || value.Length > MaxStringLength)
                throw new LanguagePackException(TextKey.LanguagePackInvalid);
            strings[key] = value;
        }
        return new LoadedLanguagePack(new Locale(code, name), fallback, strings.ToFrozenDictionary(StringComparer.Ordinal));
    }
}

internal sealed record LanguagePackFile(string Code = "", string Name = "", string? Fallback = null, Dictionary<string, string>? Strings = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(LanguagePackFile))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class PackJson : JsonSerializerContext;
