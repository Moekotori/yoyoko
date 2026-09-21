using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Chat.UI.Components;

public partial class Banner : UserControl
{
    public static readonly StyledProperty<IBrush?> FallbackProperty =
        AvaloniaProperty.Register<Banner, IBrush?>(nameof(Fallback));
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<Banner, IImage?>(nameof(Source));
    public static readonly StyledProperty<AvatarPlayback?> PlaybackProperty =
        AvaloniaProperty.Register<Banner, AvatarPlayback?>(nameof(Playback));
    public static readonly DirectProperty<Banner, bool> HasImageProperty =
        AvaloniaProperty.RegisterDirect<Banner, bool>(nameof(HasImage), control => control.HasImage);
    private Window? _window;
    private bool _attached;
    private bool _hasImage;

    static Banner()
    {
        PlaybackProperty.Changed.AddClassHandler<Banner>((control, args) =>
            control.Hook(args.OldValue as AvatarPlayback, args.NewValue as AvatarPlayback));
        SourceProperty.Changed.AddClassHandler<Banner>((control, _) => control.SyncHasImage());
    }

    public Banner() => InitializeComponent();

    public static IBrush AccentFrom(Guid id)
    {
        ReadOnlySpan<Color> swatches =
        [
            Color.FromRgb(88, 122, 158),
            Color.FromRgb(120, 98, 140),
            Color.FromRgb(72, 122, 98),
            Color.FromRgb(140, 100, 78),
            Color.FromRgb(64, 112, 128),
            Color.FromRgb(128, 86, 100),
            Color.FromRgb(90, 108, 80),
            Color.FromRgb(92, 96, 118)
        ];
        return new SolidColorBrush(swatches[(id.GetHashCode() & int.MaxValue) % swatches.Length]);
    }
    public IBrush? Fallback { get => GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }
    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public AvatarPlayback? Playback { get => GetValue(PlaybackProperty); set => SetValue(PlaybackProperty, value); }
    public bool HasImage
    {
        get => _hasImage;
        private set => SetAndRaise(HasImageProperty, ref _hasImage, value);
    }

    private void SyncHasImage() => HasImage = Source is not null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Playback?.AddWatcher();
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            window.PropertyChanged += OnWindowProperty;
            Playback?.SetWindowPaused(window.WindowState == WindowState.Minimized);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.PropertyChanged -= OnWindowProperty;
        _window = null;
        if (_attached) Playback?.RemoveWatcher();
        _attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Hook(AvatarPlayback? previous, AvatarPlayback? next)
    {
        if (previous is not null)
        {
            previous.PropertyChanged -= OnFrame;
            if (_attached) previous.RemoveWatcher();
        }
        if (next is not null)
        {
            next.PropertyChanged += OnFrame;
            Source = next.Current;
            if (_attached) next.AddWatcher();
            if (_window is not null) next.SetWindowPaused(_window.WindowState == WindowState.Minimized);
        }
        else Source = null;
    }

    private void OnFrame(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AvatarPlayback play && e.PropertyName is null or nameof(AvatarPlayback.Current))
            Source = play.Current;
    }

    private void OnWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && sender is Window window)
            Playback?.SetWindowPaused(window.WindowState == WindowState.Minimized);
    }
}
