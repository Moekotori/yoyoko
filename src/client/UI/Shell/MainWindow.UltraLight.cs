using System.ComponentModel;
using Chat.UI.Resources;

namespace Chat.UI.Shell;

public partial class MainWindow
{
    private UltraLightWindowContent? _ultraLight;
    private ShellViewModel? _ultraLightModel;

    private void InitializeUltraLight()
    {
        if (_ultraLight is not null || DataContext is not ShellViewModel shell) return;
        _ultraLightModel = shell;
        _ultraLight = new(this, () => new ShellSurface(), shell.SetUltraLightParked, UltraLightRecovery.Create);
        shell.PropertyChanged += OnUltraLightPreference;
        Closed += OnUltraLightClosed;
        _ultraLight.SetEnabled(shell.UltraLightEnabled);
    }

    private void OnUltraLightPreference(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.UltraLightEnabled))
            _ultraLight?.SetEnabled(_ultraLightModel?.UltraLightEnabled == true);
    }

    private void OnUltraLightClosed(object? sender, EventArgs e)
    {
        if (_ultraLightModel is { } shell) shell.PropertyChanged -= OnUltraLightPreference;
        _ultraLight?.Dispose();
        _ultraLight = null;
        _ultraLightModel = null;
        Closed -= OnUltraLightClosed;
    }
}
