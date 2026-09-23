using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Chat.UI.Shell;

namespace Chat.UI.Channels;

public partial class QuickSwitcher : UserControl
{
    private ShellViewModel? _subscribed;
    private IInputElement? _returnFocus;

    public QuickSwitcher()
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
        _subscribed = DataContext as ShellViewModel;
        if (_subscribed is null) return;
        _subscribed.FocusSwitcher = FocusQuery;
        _subscribed.PropertyChanged += OnShellChanged;
        if (_subscribed.SwitcherOpen) FocusQuery();
    }

    private void Unsubscribe()
    {
        if (_subscribed is null) return;
        _subscribed.PropertyChanged -= OnShellChanged;
        if (_subscribed.FocusSwitcher == FocusQuery) _subscribed.FocusSwitcher = null;
        _subscribed = null;
        _returnFocus = null;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.SwitcherOpen) && _subscribed?.SwitcherOpen == true)
            FocusQuery();
        else if (args.PropertyName == nameof(ShellViewModel.SwitcherOpen) && _subscribed?.SwitcherOpen == false)
        {
            var target = _returnFocus;
            _returnFocus = null;
            if (target is Control { IsEffectivelyVisible: true }) target.Focus();
            else if (_subscribed is { ShowChat: true, ShowSettings: false })
                _subscribed.FocusComposer?.Invoke();
        }
    }

    private void FocusQuery()
    {
        _returnFocus ??= (TopLevel.GetTopLevel(this) as Window)?.FocusManager?.GetFocusedElement();
        Dispatcher.UIThread.Post(() =>
        {
            if (_subscribed?.SwitcherOpen != true) return;
            Query.Focus();
            Query.SelectAll();
        });
    }
}
