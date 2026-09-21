using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Chat.UI.Shell;

namespace Chat.UI.Channels;

public partial class QuickSwitcher : UserControl
{
    private ShellViewModel? _subscribed;

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
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.SwitcherOpen) && _subscribed?.SwitcherOpen == true)
            FocusQuery();
    }

    private void FocusQuery() => Dispatcher.UIThread.Post(() =>
    {
        if (_subscribed?.SwitcherOpen != true) return;
        Query.Focus();
        Query.SelectAll();
    });
}
