using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Chat.Localization;
using Chat.UI.Localization;
using Chat.UI.Shell;
using FileKinds = global::Chat.Core.Messaging.FileKinds;
using PickedFile = global::Chat.Core.Sessions.PickedFile;

namespace Chat.UI.Chat;

public partial class ChatView : UserControl
{
    public static readonly StyledProperty<bool> DropActiveProperty =
        AvaloniaProperty.Register<ChatView, bool>(nameof(DropActive));
    private int _dragDepth;
    private ShellViewModel? _subscribed;
    private ScrollViewer? _scroll;
    private bool _loadingOlder;
    private bool _olderExhausted;

    public ChatView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public bool DropActive
    {
        get => GetValue(DropActiveProperty);
        set => SetValue(DropActiveProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _olderExhausted = false;
        if (VisualRoot is not null) Subscribe();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachScroll();
        if (_scroll is null)
            Messages.LayoutUpdated += OnMessagesLayout;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        Messages.LayoutUpdated -= OnMessagesLayout;
        if (_scroll is not null)
            _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = null;
        base.OnUnloaded(e);
    }

    private void Subscribe()
    {
        Unsubscribe();
        _subscribed = DataContext as ShellViewModel;
        if (_subscribed is null) return;
        _subscribed.ScrollToLatest = ScrollToEnd;
        _subscribed.IsNearBottom = NearBottom;
        _subscribed.PropertyChanged += OnShellChanged;
        _subscribed.IsNearBottom = IsNearBottom;
        Dispatcher.UIThread.Post(() =>
        {
            if (_subscribed is null || _scroll is not null) return;
            _scroll = Messages.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;
        });
    }

    private void Unsubscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnShellChanged;
            if (_subscribed.IsNearBottom == IsNearBottom) _subscribed.IsNearBottom = null;
            if (_subscribed.ScrollToLatest == ScrollToEnd) _subscribed.ScrollToLatest = null;
            if (_subscribed.IsNearBottom == NearBottom) _subscribed.IsNearBottom = null;
        }
        if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = null;
        _subscribed = null;
    }
    private bool IsNearBottom() => _scroll is null || _scroll.Extent.Height - _scroll.Viewport.Height - _scroll.Offset.Y < 72;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.SearchOpen) && _subscribed?.SearchOpen == true)
            Dispatcher.UIThread.Post(() => { if (_subscribed?.SearchOpen == true) SearchBox.Focus(); });
        if (args.PropertyName == nameof(ShellViewModel.SelectedChannel))
            _olderExhausted = false;
    }

    private void OnMessagesLayout(object? sender, EventArgs e)
    {
        AttachScroll();
        if (_scroll is not null)
            Messages.LayoutUpdated -= OnMessagesLayout;
    }

    private void AttachScroll()
    {
        if (_scroll is not null) return;
        _scroll = Messages.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroll is null) return;
        _scroll.ScrollChanged += OnScrollChanged;
    }

    private bool NearBottom()
    {
        if (_scroll is null) return true;
        return _scroll.Extent.Height - _scroll.Viewport.Height - _scroll.Offset.Y <= 80;
    }

    private void ScrollToEnd()
    {
        if (Messages.ItemCount == 0) return;
        Messages.ScrollIntoView(Messages.ItemCount - 1);
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_loadingOlder || _scroll is null || DataContext is not ShellViewModel shell) return;
        if (_scroll.Offset.Y > 36)
        {
            _olderExhausted = false;
            return;
        }
        if (_olderExhausted || !shell.HasOlder) return;
        var anchor = shell.VisibleMessages.FirstOrDefault() ?? shell.Messages.FirstOrDefault();
        var count = shell.Messages.Count;
        _loadingOlder = true;
        try
        {
            await shell.LoadOlderAsync();
            if (shell.Messages.Count == count) _olderExhausted = true;
            if (anchor is not null)
                Messages.ScrollIntoView(anchor);
        }
        finally { _loadingOlder = false; }
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ShellViewModel shell) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var chord = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var send = shell.EnterToSend ? !shift && !chord : chord;
        if (!send) return;
        e.Handled = true;
        if (shell.CanSend && shell.Send.CanExecute(null))
            shell.Send.Execute(null);
    }

    private async void CopyMessage(object? sender, RoutedEventArgs args)
    {
        if (sender is not Control { DataContext: MessageRow row }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        try { if (clipboard is not null) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, row.Content); }
        catch (Exception) { _subscribed?.Workspace.ShowNotice(I18n.Presenter.Get(TextKey.ClipboardFailed)); }
        args.Handled = true;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        _dragDepth++;
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        DropActive = e.DragEffects == DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (--_dragDepth <= 0)
        {
            _dragDepth = 0;
            DropActive = false;
        }
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _dragDepth = 0;
        DropActive = false;
        if (DataContext is not ShellViewModel shell) return;
        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return;
        var files = new List<PickedFile>();
        foreach (var item in items)
        {
            if (item is not IStorageFile file) continue;
            var path = file.TryGetLocalPath();
            Stream stream;
            long size;
            if (path is not null && System.IO.File.Exists(path))
            {
                var info = new FileInfo(path);
                size = info.Length;
                stream = System.IO.File.OpenRead(path);
            }
            else
            {
                var props = await file.GetBasicPropertiesAsync();
                size = (long)(props.Size ?? 0);
                stream = await file.OpenReadAsync();
            }
            files.Add(new(file.Name, FileKinds.MimeFromFileName(file.Name), stream, size));
        }
        await shell.QueueFilesAsync(files);
    }
}
