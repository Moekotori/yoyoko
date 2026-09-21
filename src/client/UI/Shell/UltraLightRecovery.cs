using Avalonia.Controls;
using Avalonia.Layout;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Shell;

internal static class UltraLightRecovery
{
    public static Control Create(Action retry) => new StackPanel
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 16,
        Children =
        {
            new TextBlock { Text = I18n.T(TextKey.UltraLightRestoreFailed) },
            new Button
            {
                Content = I18n.T(TextKey.UltraLightRestoreRetry),
                HorizontalAlignment = HorizontalAlignment.Center,
                Command = new ActionCommand(_ => retry())
            }
        }
    };
}
