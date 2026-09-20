using System.Globalization;

namespace Chat.Localization;

public readonly record struct Locale(string Code, string NativeName)
{
    public static readonly Locale Chinese = new("zh-Hans", "简体中文");
    public static readonly Locale English = new("en", "English");
    public static readonly Locale Japanese = new("ja", "日本語");
    public static IReadOnlyList<Locale> Supported { get; } = [Chinese, English, Japanese];

    public static Locale Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return FromSystem();
        foreach (var locale in Supported)
            if (string.Equals(locale.Code, value, StringComparison.OrdinalIgnoreCase)) return locale;
        if (value.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return Chinese;
        if (value.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return Japanese;
        if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return English;
        return English;
    }

    public static Locale FromSystem()
    {
        for (var culture = CultureInfo.CurrentUICulture;
             !string.IsNullOrEmpty(culture.Name) && culture != CultureInfo.InvariantCulture;
             culture = culture.Parent)
        {
            foreach (var locale in Supported)
                if (string.Equals(locale.Code, culture.Name, StringComparison.OrdinalIgnoreCase)) return locale;
        }
        return Parse(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
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
    string Get(Locale locale, string key, params object[] args);
    IReadOnlyCollection<string> Keys { get; }
    bool SameKeys();
}
