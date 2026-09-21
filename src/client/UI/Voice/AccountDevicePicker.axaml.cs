using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Chat.UI.Voice;

public partial class AccountDevicePicker : UserControl
{
    public static readonly StyledProperty<IEnumerable<AudioDeviceChoice>?> ChoicesProperty =
        AvaloniaProperty.Register<AccountDevicePicker, IEnumerable<AudioDeviceChoice>?>(nameof(Choices));
    public static readonly StyledProperty<AudioDeviceChoice?> SelectedDeviceProperty =
        AvaloniaProperty.Register<AccountDevicePicker, AudioDeviceChoice?>(nameof(SelectedDevice), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<AccountDevicePicker, string>(nameof(Title), "");
    public static readonly StyledProperty<string> ErrorProperty = AvaloniaProperty.Register<AccountDevicePicker, string>(nameof(Error), "");
    public static readonly StyledProperty<bool> HasErrorProperty = AvaloniaProperty.Register<AccountDevicePicker, bool>(nameof(HasError));
    public IEnumerable<AudioDeviceChoice>? Choices { get => GetValue(ChoicesProperty); set => SetValue(ChoicesProperty, value); }
    public AudioDeviceChoice? SelectedDevice { get => GetValue(SelectedDeviceProperty); set => SetValue(SelectedDeviceProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }
    public bool HasError { get => GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }
    public event EventHandler? Chosen;
    public AccountDevicePicker() => InitializeComponent();
    private void OnChoose(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is Visual source && source.GetVisualAncestors().OfType<ListBoxItem>().Any())
            Chosen?.Invoke(this, EventArgs.Empty);
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedDevice is not null) { Chosen?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    }
}
