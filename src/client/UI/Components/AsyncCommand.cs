using System.Windows.Input;

namespace Chat.UI.Components;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Action<Exception> _onError;
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public AsyncCommand(Func<Task> execute, Action<Exception> onError)
        : this(_ => execute(), onError) { }
    public AsyncCommand(Func<object?, Task> execute, Action<Exception> onError)
    {
        _execute = execute;
        _onError = onError;
    }
    public bool CanExecute(object? parameter) => !_running;
    public async void Execute(object? parameter)
    {
        if (_running) return;
        _running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _execute(parameter); }
        catch (Exception exception) { _onError(exception); }
        finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
