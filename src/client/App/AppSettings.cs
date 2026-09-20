using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chat.App;

internal sealed record AppSettings(string ProductName = "LightChat", string CacheDirectory = "")
{
    public static AppSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings)
            ?? new AppSettings();
        return settings with
        {
            ProductName = Environment.GetEnvironmentVariable("CHAT_PRODUCT_NAME") ?? settings.ProductName,
            CacheDirectory = Environment.GetEnvironmentVariable("CHAT_CACHE_DIRECTORY") ?? settings.CacheDirectory
        };
    }
    public string CachePath => Path.Combine(string.IsNullOrWhiteSpace(CacheDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "chat-desktop")
        : CacheDirectory, "cache.db");
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext;
