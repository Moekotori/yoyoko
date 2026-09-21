using Avalonia;
using Avalonia.Styling;

namespace Chat.UI.Appearance;

public static class ColorSchemeApply
{
    public static void Apply(ColorScheme scheme)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = scheme == ColorScheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
