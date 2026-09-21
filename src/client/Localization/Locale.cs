using System.Globalization;

namespace Chat.Localization;

public readonly record struct Locale(string Code, string NativeName)
{
    public static readonly Locale Chinese = new("zh-Hans", "简体中文");
    public static readonly Locale English = new("en", "English");
    public static readonly Locale Japanese = new("ja", "日本語");
    public static IReadOnlyList<Locale> Supported { get; } = [Chinese, English, Japanese];
    public static bool IsBuiltIn(string code) =>
        code.Equals(English.Code, StringComparison.OrdinalIgnoreCase) ||
        code.Equals(Chinese.Code, StringComparison.OrdinalIgnoreCase) ||
        code.Equals(Japanese.Code, StringComparison.OrdinalIgnoreCase);

    public static Locale Parse(string? value, IReadOnlyList<Locale>? extra = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return FromSystem(extra);
        if (Match(value, extra) is { } exact) return exact;
        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return Chinese;
        if (value.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return Japanese;
        if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return English;
        if (extra is not null)
            foreach (var locale in extra)
                if (value.StartsWith(locale.Code + "-", StringComparison.OrdinalIgnoreCase)) return locale;
        return English;
    }

    public static Locale FromSystem(IReadOnlyList<Locale>? extra = null)
    {
        for (var culture = CultureInfo.CurrentUICulture;
             !string.IsNullOrEmpty(culture.Name) && culture != CultureInfo.InvariantCulture;
             culture = culture.Parent)
        {
            if (Match(culture.Name, extra) is { } match) return match;
        }
        return Parse(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, extra);
    }

    private static Locale? Match(string value, IReadOnlyList<Locale>? extra)
    {
        foreach (var locale in Supported)
            if (string.Equals(locale.Code, value, StringComparison.OrdinalIgnoreCase)) return locale;
        if (extra is not null)
            foreach (var locale in extra)
                if (string.Equals(locale.Code, value, StringComparison.OrdinalIgnoreCase)) return locale;
        return null;
    }

    public void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo(Code);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

public interface ILocalePreference
{
    Locale Current { get; }
    void Set(Locale locale);
    event Action<Locale>? Changed;
}

public sealed class MemoryLocalePreference : ILocalePreference
{
    public MemoryLocalePreference(Locale current) => Current = current;
    public Locale Current { get; private set; }
    public event Action<Locale>? Changed;
    public void Set(Locale locale)
    {
        if (locale == Current) return;
        Current = locale;
        locale.ApplyCulture();
        Changed?.Invoke(locale);
    }
}

public interface ITextCatalog
{
    string Get(Locale locale, string key);
    string Get(Locale locale, string key, params object[] args);
    IReadOnlyCollection<string> Keys { get; }
    IReadOnlyList<Locale> Available { get; }
    bool SameKeys();
    event Action? Changed;
}
