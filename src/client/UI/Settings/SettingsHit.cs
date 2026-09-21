using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Chat.UI.Settings;

internal static class SettingsHit
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(SettingsHit));
    public static readonly AttachedProperty<object?> ParameterProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("Parameter", typeof(SettingsHit));

    static SettingsHit() => CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);

    public static ICommand? GetCommand(Control control) => control.GetValue(CommandProperty);
    public static void SetCommand(Control control, ICommand? value) => control.SetValue(CommandProperty, value);
    public static object? GetParameter(Control control) => control.GetValue(ParameterProperty);
    public static void SetParameter(Control control, object? value) => control.SetValue(ParameterProperty, value);

    internal static bool FromInteractive(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is ToggleSwitch or Button or ComboBox or Slider or TextBox or Thumb) return true;
        return false;
    }

    private static void OnCommandChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.Tapped -= OnTapped;
        control.KeyDown -= OnKeyDown;
        if (args.NewValue is not ICommand) return;
        control.Tapped += OnTapped;
        control.KeyDown += OnKeyDown;
        control.Focusable = true;
    }

    private static void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control || GetCommand(control) is not { } command) return;
        if (FromInteractive(e.Source)) return;
        var parameter = GetParameter(control);
        if (!command.CanExecute(parameter)) return;
        control.Focus();
        command.Execute(parameter);
        e.Handled = true;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        if (sender is not Control control || GetCommand(control) is not { } command) return;
        if (e.Source != sender && FromInteractive(e.Source)) return;
        var parameter = GetParameter(control);
        if (!command.CanExecute(parameter)) return;
        command.Execute(parameter);
        e.Handled = true;
    }
}
