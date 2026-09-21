using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Chat.UI.Resources;

// One low-frequency sampler per window. Input/focus events reuse the last sample.
public sealed class WindowResourceGovernor : IDisposable
{
    private readonly Window _window;
    private readonly Action<VisualResourceBudget> _apply;
    private readonly VisualResourcePolicy _policy = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private TimeSpan _lastInput;
    private VisualResourceBudget? _current;

    public WindowResourceGovernor(Window window, Action<VisualResourceBudget> apply)
    {
        _window = window;
        _apply = apply;
        window.PropertyChanged += OnWindow;
        window.Closed += OnClosed;
        window.AddHandler(InputElement.KeyDownEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerMovedEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, OnInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        _timer.Tick += OnSample;
        _timer.Start();
        OnSample(null, EventArgs.Empty);
    }

    private void OnInput(object? sender, RoutedEventArgs e)
    {
        _lastInput = _clock.Elapsed;
        Apply();
    }

    private void OnWindow(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty && e.Property != Visual.IsVisibleProperty
            && e.Property != Window.IsActiveProperty) return;
        if (_window.IsActive) _lastInput = _clock.Elapsed;
        Apply();
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized) _timer.Start();
        else _timer.Stop();
    }

    private void OnSample(object? sender, EventArgs e)
    {
        _process.Refresh();
        var memory = GC.GetGCMemoryInfo();
        // Last-GC system load can be stale; do not force a collection to refresh it.
        var load = memory.TotalAvailableMemoryBytes > 0
            ? (double)memory.MemoryLoadBytes / memory.TotalAvailableMemoryBytes : 0;
        _policy.Sample(_clock.Elapsed, _process.WorkingSet64, load);
        Apply();
    }

    private void Apply()
    {
        var next = _policy.Resolve(_window.IsVisible && _window.WindowState != WindowState.Minimized,
            _window.IsActive, _clock.Elapsed - _lastInput);
        if (next == _current) return;
        _current = next;
        _apply(next);
    }

    private void OnClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnSample;
        _window.PropertyChanged -= OnWindow;
        _window.Closed -= OnClosed;
        _window.RemoveHandler(InputElement.KeyDownEvent, OnInput);
        _window.RemoveHandler(InputElement.PointerPressedEvent, OnInput);
        _window.RemoveHandler(InputElement.PointerMovedEvent, OnInput);
        _window.RemoveHandler(InputElement.PointerWheelChangedEvent, OnInput);
        _process.Dispose();
    }
}
