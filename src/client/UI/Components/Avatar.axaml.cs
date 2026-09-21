using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Chat.UI.Components;

public partial class Avatar : UserControl
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<Avatar, IImage?>(nameof(Source));
    public static readonly StyledProperty<AvatarPlayback?> PlaybackProperty = AvaloniaProperty.Register<Avatar, AvatarPlayback?>(nameof(Playback));
    public static readonly StyledProperty<string> InitialProperty = AvaloniaProperty.Register<Avatar, string>(nameof(Initial), "");
    public static readonly StyledProperty<bool> IsOnlineProperty = AvaloniaProperty.Register<Avatar, bool>(nameof(IsOnline));
    public static readonly DirectProperty<Avatar, bool> HasImageProperty =
        AvaloniaProperty.RegisterDirect<Avatar, bool>(nameof(HasImage), control => control.HasImage);
    private Window? _window;
    private bool _attached;
    private bool _hasImage;
    static Avatar()
    {
        PlaybackProperty.Changed.AddClassHandler<Avatar>((control, args) =>
            control.Hook(args.OldValue as AvatarPlayback, args.NewValue as AvatarPlayback));
        SourceProperty.Changed.AddClassHandler<Avatar>((control, _) => control.SyncHasImage());
    }
    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public AvatarPlayback? Playback { get => GetValue(PlaybackProperty); set => SetValue(PlaybackProperty, value); }
    public string Initial { get => GetValue(InitialProperty); set => SetValue(InitialProperty, value); }
    public bool IsOnline { get => GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }
    public bool HasImage
    {
        get => _hasImage;
        private set => SetAndRaise(HasImageProperty, ref _hasImage, value);
    }
    public Avatar() => InitializeComponent();

    public static string FromName(string? name)
    {
        var value = name?.Trim() ?? "";
        if (value.Length == 0) return "";
        var initial = StringInfo.GetNextTextElement(value);
        return initial.Length == 1 ? initial.ToUpperInvariant() : initial;
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
