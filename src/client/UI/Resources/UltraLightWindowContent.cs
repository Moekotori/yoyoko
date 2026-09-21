using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Chat.UI.Resources;

// Owns only Window.Content. It never closes the Window or disposes its DataContext.
public sealed class UltraLightWindowContent : IDisposable
{
    private readonly Window _window;
    private readonly Func<Control> _create;
    private readonly Action<bool> _parked;
    private bool _enabled;
    private bool _disposed;
    public bool IsParked { get; private set; }

    public UltraLightWindowContent(Window window, Func<Control> create, Action<bool> parked)
    {
        _window = window;
        _create = create;
        _parked = parked;
        window.PropertyChanged += OnWindow;
        window.Closed += OnClosed;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Reconcile();
    }

    private void OnWindow(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty) Reconcile();
    }

    private void Reconcile()
    {
        if (_disposed) return;
        var visible = _window.IsVisible && _window.WindowState != WindowState.Minimized;
        if (visible && IsParked)
        {
            // Build before changing state so a construction failure retains the retryable parked state.
            var content = _create();
            IsParked = false;
            _parked(false);
            _window.Content = content;
        }
        else if (!visible && _enabled && !IsParked)
        {
            if (_window.Content is Control content)
            {
                foreach (var control in content.GetVisualDescendants().OfType<Control>().ToArray())
                {
                    if (control is Button button) button.Flyout?.Hide();
                    control.ContextMenu?.Close();
                    control.ContextFlyout?.Hide();
                }
                _window.Content = null;
                content.DataContext = null;
            }
            IsParked = true;
            _parked(true);
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.PropertyChanged -= OnWindow;
        _window.Closed -= OnClosed;
    }
}
