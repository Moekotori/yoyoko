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
    private DispatcherOperation? _pendingScroll;

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
        _subscribed.FocusComposer = FocusComposer;
        _subscribed.FocusSearch = FocusSearch;
        _subscribed.PropertyChanged += OnShellChanged;
        if (IsLoaded) AttachScroll();
    }

    private void Unsubscribe()
    {
        _pendingScroll?.Abort();
        _pendingScroll = null;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnShellChanged;
            if (_subscribed.ScrollToLatest == ScrollToEnd) _subscribed.ScrollToLatest = null;
            if (_subscribed.IsNearBottom == NearBottom) _subscribed.IsNearBottom = null;
            if (_subscribed.FocusComposer == FocusComposer) _subscribed.FocusComposer = null;
            if (_subscribed.FocusSearch == FocusSearch) _subscribed.FocusSearch = null;
        }
        if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = null;
        _subscribed = null;
    }

    private void FocusComposer() => Dispatcher.UIThread.Post(() =>
    {
        if (!Composer.IsVisible) return;
        Composer.Focus();
        Composer.CaretIndex = Composer.Text?.Length ?? 0;
    });

    private void FocusSearch() => Dispatcher.UIThread.Post(() =>
    {
        if (_subscribed?.SearchOpen != true) return;
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
    });

    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.SearchOpen) && _subscribed?.SearchOpen == true)
            FocusSearch();
        if (args.PropertyName == nameof(ShellViewModel.SelectedChannel))
        {
            _olderExhausted = false;
            _pendingScroll?.Abort();
            _pendingScroll = null;
        }
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
        if (_pendingScroll is not null) return;
        var shell = _subscribed;
        var channel = shell?.SelectedChannel;
        _pendingScroll = Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pendingScroll = null;
            if (!IsEffectivelyVisible || shell != _subscribed ||
                !ReferenceEquals(channel, shell?.SelectedChannel) || Messages.ItemCount == 0) return;
            // Collection and virtualization layout must settle before using the extent.
            AttachScroll();
            _scroll?.ScrollToEnd();
        }, DispatcherPriority.Loaded);
    }

    private async void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_loadingOlder || _scroll is null || DataContext is not ShellViewModel shell) return;
        if (_scroll.Offset.Y > 36)
        {
            _olderExhausted = false;
            return;
        }
        if (_olderExhausted || shell.IsChannelLoading || !shell.HasOlder) return;
        var channel = shell.SelectedChannel;
        var anchor = shell.VisibleMessages.FirstOrDefault() ?? shell.Messages.FirstOrDefault();
        var count = shell.Messages.Count;
        _loadingOlder = true;
        try
        {
            await shell.LoadOlderAsync();
            if (!ReferenceEquals(channel, shell.SelectedChannel)) return;
            if (shell.Messages.Count == count) _olderExhausted = true;
            if (anchor is not null)
                Messages.ScrollIntoView(anchor);
        }
        finally { _loadingOlder = false; }
    }

    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel shell) return;
        var chord = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.V && chord)
        {
            if (await TryPasteFilesAsync(shell)) e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter) return;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var send = shell.EnterToSend ? !shift && !chord : chord;
        if (!send) return;
        e.Handled = true;
        if (shell.CanSend && shell.Send.CanExecute(null))
            shell.Send.Execute(null);
    }

    private async Task<bool> TryPasteFilesAsync(ShellViewModel shell)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return false;
        IReadOnlyList<IStorageItem>? items;
        try { items = await clipboard.TryGetFilesAsync(); }
        catch (Exception) { return false; }
        if (items is null || items.Count == 0) return false;
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
        if (files.Count == 0) return false;
        await shell.QueueFilesAsync(files);
        return true;
    }

    private async void CopyMessage(object? sender, RoutedEventArgs args)
    {
        if (sender is not Control { DataContext: MessageRow row }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        try { if (clipboard is not null) await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, row.Content); }
        catch (Exception) { _subscribed?.Workspace.ShowNotice(I18n.T(TextKey.ClipboardFailed)); }
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
