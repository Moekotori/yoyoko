using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Chat.UI.Resources;

// Owns only Window.Content. No window close, account/session disposal or media calls.
public sealed class UltraLightWindowContent : IDisposable
{
    private readonly Window _window;
    private readonly Func<Control> _create;
    private readonly Action<bool> _parked;
    private readonly Func<Action, Control> _recovery;
    private bool _enabled;
    private bool _disposed;
    private bool _transitioning;
    private bool _parkingBlocked;
    public bool IsParked { get; private set; }
    public Exception? LastRestoreError { get; private set; }

    public UltraLightWindowContent(Window window, Func<Control> create, Action<bool> parked,
        Func<Action, Control> recovery)
    {
        _window = window;
        _create = create;
        _parked = parked;
        _recovery = recovery;
        window.PropertyChanged += OnWindow;
        window.Closed += OnClosed;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !_enabled)
        {
            _parkingBlocked = false;
            if (!IsParked) LastRestoreError = null;
        }
        _enabled = enabled;
        Reconcile();
    }

    public void RetryRestore()
    {
        if (_disposed || _transitioning || !IsParked || !Visible) return;
        Restore();
    }

    private bool Visible => _window.IsVisible && _window.WindowState != WindowState.Minimized;

    private void OnWindow(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty) Reconcile();
    }

    private void Reconcile()
    {
        if (_disposed || _transitioning) return;
        if (Visible && IsParked && LastRestoreError is null) Restore();
        else if (!Visible && _enabled && !_parkingBlocked && !IsParked) Park();
    }

    private void Restore()
    {
        _transitioning = true;
        Control? content = null;
        try
        {
            content = _create();
            if (_disposed || !Visible) return;
            _parked(false);
            if (_disposed || !Visible)
            {
                if (!_disposed) _parked(true);
                return;
            }
            _window.Content = content;
            if (_disposed || !Visible)
            {
                _window.Content = null;
                if (!_disposed) _parked(true);
                return;
            }
            IsParked = false;
            LastRestoreError = null;
            content = null; // Window now owns the successfully attached view.
        }
        catch (Exception error)
        {
            LastRestoreError = error;
            _parkingBlocked = true;
            IsParked = true;
            if (!_disposed)
            {
                try { _parked(true); } catch { /* Do not let UI projection errors end the session. */ }
                // Always leave a visible recovery route. No automatic retry loop.
                _window.Content = RecoveryControl();
            }
        }
        finally
        {
            ReleaseView(content);
            _transitioning = false;
        }
    }

    private void Park()
    {
        _transitioning = true;
        var content = _window.Content as Control;
        try
        {
            if (content is not null)
                foreach (var control in content.GetVisualDescendants().OfType<Control>().ToArray())
                {
                    if (control is Button button) button.Flyout?.Hide();
                    control.ContextMenu?.Close();
                    control.ContextFlyout?.Hide();
                }
            _window.Content = null; // Detach captures viewport and removes view subscriptions first.
            _parked(true);
            IsParked = true;
            LastRestoreError = null;
            ReleaseView(content);
        }
        catch (Exception error)
        {
            LastRestoreError = error;
            _parkingBlocked = true;
            if (!_disposed)
            {
                try { _parked(false); } catch { /* Retain the existing view and business owner. */ }
                _window.Content = content;
            }
        }
        finally { _transitioning = false; }
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    private void ReleaseView(Control? content)
    {
        try { if (content is not null) content.DataContext = null; }
        catch { _parkingBlocked = true; } // A detach notification must not strand the transition lock.
    }

    private Control RecoveryControl()
    {
        try { return _recovery(RetryRestore); }
        catch
        {
            // Even a broken theme/catalog in the richer error view must leave a retry action.
            var button = new Button { Content = "Retry" };
            button.Click += (_, _) => RetryRestore();
            return button;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.PropertyChanged -= OnWindow;
        _window.Closed -= OnClosed;
    }
}
