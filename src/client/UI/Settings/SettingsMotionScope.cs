using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Chat.UI.Settings;

// Owns only visual animation eligibility; no timers or business state.
internal sealed class SettingsMotionScope : IDisposable
{
    private readonly SettingsView _view;
    private readonly Window? _window;
    private readonly Visual[] _ancestors;
    private SettingsViewModel? _model;

    public SettingsMotionScope(SettingsView view)
    {
        _view = view;
        _window = TopLevel.GetTopLevel(view) as Window;
        _ancestors = view.GetVisualAncestors().Prepend(view).ToArray();
        view.DataContextChanged += OnContextChanged;
        foreach (var visual in _ancestors) visual.PropertyChanged += OnVisibilityChanged;
        if (_window is not null) _window.PropertyChanged += OnWindowChanged;
        BindContext();
    }

    private void OnContextChanged(object? sender, EventArgs e) => BindContext();
    private void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty) Refresh();
    }
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty) Refresh();
    }

    private void BindContext()
    {
        if (_model is not null) _model.PropertyChanged -= OnPreferenceChanged;
        _model = _view.DataContext as SettingsViewModel;
        if (_model is not null) _model.PropertyChanged += OnPreferenceChanged;
        Refresh();
    }

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(SettingsViewModel.ReduceMotion)) Refresh();
    }

    internal void Refresh() => _view.Classes.Set("motion", _model is { ReduceMotion: false }
        && _view.IsEffectivelyVisible && _window?.WindowState != WindowState.Minimized);

    public void Dispose()
    {
        _view.DataContextChanged -= OnContextChanged;
        foreach (var visual in _ancestors) visual.PropertyChanged -= OnVisibilityChanged;
        if (_window is not null) _window.PropertyChanged -= OnWindowChanged;
        if (_model is not null) _model.PropertyChanged -= OnPreferenceChanged;
        _view.Classes.Remove("motion");
    }
}
