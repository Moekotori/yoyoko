using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Chat.UI.Components;

public partial class Avatar : UserControl
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<Avatar, IImage?>(nameof(Source));
    public static readonly StyledProperty<bool> IsOnlineProperty = AvaloniaProperty.Register<Avatar, bool>(nameof(IsOnline));
    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool IsOnline { get => GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }
    public Avatar() => InitializeComponent();
}
