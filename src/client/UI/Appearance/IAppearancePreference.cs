using Chat.UI.Components;

namespace Chat.UI.Appearance;

public enum ColorScheme { Dark, Light }

public sealed class ColorSchemeChoice(ColorScheme scheme) : ObservableObject
{
    private string _name = "";
    public ColorScheme Scheme { get; } = scheme;
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; Changed(); }
    }
}

public interface IAppearancePreference
{
    ColorScheme ColorScheme { get; }
    bool WallpaperEnabled { get; }
    string WallpaperFile { get; }
    string WallpaperLabel { get; }
    string WallpaperDirectory { get; }
    int WallpaperBlur { get; }
    int WallpaperBrightness { get; }
    event Action? Changed;
    void SetColorScheme(ColorScheme value);
    void SetWallpaperEnabled(bool value);
    void SetWallpaperFile(string file, string label);
    void SetWallpaperBlur(int value);
    void SetWallpaperBrightness(int value);
    void Flush();
}
