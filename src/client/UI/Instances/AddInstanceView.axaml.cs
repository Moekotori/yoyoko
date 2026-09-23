using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Chat.UI.Shell;

namespace Chat.UI.Instances;

public partial class AddInstanceView : UserControl
{
    private ShellViewModel? _model;
    private IInputElement? _returnFocus;

    public AddInstanceView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VisualRoot is not null) Subscribe();
    }

    private void Subscribe()
    {
        Unsubscribe();
        _model = DataContext as ShellViewModel;
        if (_model is null) return;
        _model.PropertyChanged += OnModelChanged;
        if (_model.ShowAddInstance) FocusAddress();
    }

    private void Unsubscribe()
    {
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = null;
        _returnFocus = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.ShowAddInstance)) return;
        if (_model?.ShowAddInstance == true) FocusAddress();
        else { _returnFocus?.Focus(); _returnFocus = null; }
    }

    private void FocusAddress()
    {
        _returnFocus ??= (TopLevel.GetTopLevel(this) as Window)?.FocusManager?.GetFocusedElement();
        Dispatcher.UIThread.Post(() =>
        {
            if (_model?.ShowAddInstance != true || !IsEffectivelyVisible) return;
            AddressInput.Focus();
            AddressInput.SelectAll();
        }, DispatcherPriority.Loaded);
    }
}
