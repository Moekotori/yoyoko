using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Chat.UI.Shell;

namespace Chat.UI.Chat;

// Owns only the drawer transition, focus and dismissal; profile operations stay in Core.
public partial class ProfileDrawer : UserControl
{
    private ShellViewModel? _model;
    private Window? _window;
    private IInputElement? _returnFocus;
    private CancellationTokenSource? _closing;

    public ProfileDrawer()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => Unbind();
        DataContextChanged += (_, _) => { if (VisualRoot is not null) Bind(); };
    }

    private void Bind()
    {
        Unbind();
        _model = DataContext as ShellViewModel;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        if (_window is not null) _window.PropertyChanged += OnWindowChanged;
        Refresh();
    }

    private void Unbind()
    {
        _closing?.Cancel();
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        if (_window is not null) _window.PropertyChanged -= OnWindowChanged;
        _model = null;
        _window = null;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.ProfileOpen) or nameof(ShellViewModel.ReduceMotion)) Refresh();
    }
    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty) Refresh();
    }

    private void Refresh()
    {
        _closing?.Cancel();
        _closing = null;
        var animate = _model is { ReduceMotion: false } && _window is { IsVisible: true, WindowState: not WindowState.Minimized };
        Classes.Set("motion", animate);
        if (_model?.ProfileOpen == true)
        {
            var wasOpen = Classes.Contains("open");
            if (!wasOpen) _returnFocus = _window?.FocusManager?.GetFocusedElement();
            IsVisible = true;
            Classes.Add("open");
            if (!wasOpen) Dispatcher.UIThread.Post(() => { if (_model?.ProfileOpen == true) DisplayNameField.Focus(); }, DispatcherPriority.Loaded);
        }
        else
        {
            var wasOpen = Classes.Contains("open");
            Classes.Remove("open");
            if (wasOpen) { _returnFocus?.Focus(); _returnFocus = null; }
            if (!animate) IsVisible = false;
            else
            {
                var closing = new CancellationTokenSource();
                _closing = closing;
                _ = HideAfterTransitionAsync(closing);
            }
        }
    }

    private async Task HideAfterTransitionAsync(CancellationTokenSource closing)
    {
        try
        {
            await Task.Delay(230, closing.Token);
            if (_model?.ProfileOpen != true) IsVisible = false;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_closing, closing)) _closing = null;
            closing.Dispose();
        }
    }

    private void OnBackdrop(object? sender, PointerPressedEventArgs e)
    {
        _model?.CloseProfile.Execute(null);
        e.Handled = true;
    }
}
