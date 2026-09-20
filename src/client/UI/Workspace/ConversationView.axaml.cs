using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Chat.Localization;
using Chat.UI.Localization;

namespace Chat.UI.Workspace;

public partial class ConversationView : UserControl
{
    private WorkspaceViewModel? _subscribed;
    private CancellationTokenSource? _transition;

    public ConversationView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        DataContextChanged += (_, _) => { if (VisualRoot is not null) Subscribe(); };
    }

    private void Subscribe()
    {
        Unsubscribe();
        _subscribed = DataContext as WorkspaceViewModel;
        if (_subscribed is not null) _subscribed.PropertyChanged += OnWorkspaceChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnWorkspaceChanged;
        _subscribed = null;
        _transition?.Cancel();
        _transition?.Dispose();
        _transition = null;
    }

    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceViewModel.SearchVisible) && _subscribed?.SearchVisible == true)
            Dispatcher.UIThread.Post(() => { if (_subscribed?.SearchVisible == true) SearchInput.Focus(); });
        if (args.PropertyName == nameof(WorkspaceViewModel.SelectedChannel))
        {
            _transition?.Cancel();
            _transition?.Dispose();
            _transition = new();
            _ = FadeTimelineAsync(_transition.Token);
        }
    }

    private async Task FadeTimelineAsync(CancellationToken cancellationToken)
    {
        if (VisualRoot is null || TopLevel.GetTopLevel(this) is Window { WindowState: WindowState.Minimized }) return;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(120),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.65) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } }
            }
        };
        try { await animation.RunAsync(TimelineSurface, cancellationToken); }
        catch (OperationCanceledException) { }
    }

    private async void CopyMessage(object? sender, RoutedEventArgs args)
    {
        if (sender is not Control { DataContext: MessageItem message }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) { _subscribed?.ShowNotice(I18n.Presenter.Get(TextKey.ClipboardUnavailable)); return; }
        try { await clipboard.SetTextAsync(message.Text); }
        catch (Exception) { _subscribed?.ShowNotice(I18n.Presenter.Get(TextKey.ClipboardFailed)); }
        args.Handled = true;
    }
}
