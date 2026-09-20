using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using FileKinds = global::Chat.Core.Messaging.FileKinds;
using PickedFile = global::Chat.Core.Sessions.PickedFile;
using Chat.UI.Shell;

namespace Chat.UI.Chat;

public partial class ChatView : UserControl
{
    public static readonly StyledProperty<bool> DropActiveProperty =
        AvaloniaProperty.Register<ChatView, bool>(nameof(DropActive));
    private int _dragDepth;

    public ChatView()
    {
        InitializeComponent();
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
        if (DataContext is ShellViewModel shell)
            shell.ScrollToLatest = ScrollToEnd;
    }

    private void ScrollToEnd()
    {
        if (Messages.ItemCount == 0) return;
        Messages.ScrollIntoView(Messages.ItemCount - 1);
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
