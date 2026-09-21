using System.ComponentModel;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;

namespace Chat.UI.Settings;

public partial class SettingsView : UserControl
{
    private SettingsMotionScope? _motion;
    private SettingsViewModel? _model;
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindModel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _motion = new(this);
        BindModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnbindModel();
        _motion?.Dispose();
        _motion = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void BindModel()
    {
        if (ReferenceEquals(_model, DataContext)) return;
        UnbindModel();
        _model = DataContext as SettingsViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
    }

    private void UnbindModel()
    {
        if (_model is null) return;
        _model.PropertyChanged -= OnModelChanged;
        _model = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.Section) or null or "")
            SettingsScroll.Offset = default;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SettingsViewModel settings)
        {
            if (settings.Shortcuts.IsRecording)
            {
                settings.Shortcuts.CancelCapture();
                e.Handled = true;
                return;
            }
            settings.Close.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
