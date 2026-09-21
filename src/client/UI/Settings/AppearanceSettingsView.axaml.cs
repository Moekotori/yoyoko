using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Chat.Core.Messaging;
using Chat.Core.Sessions;
using Chat.UI.Appearance;

namespace Chat.UI.Settings;

public partial class AppearanceSettingsView : UserControl
{
    private int _dragDepth;

    public AppearanceSettingsView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnColorTapped(object? sender, TappedEventArgs e)
    {
        if (SettingsHit.FromInteractive(e.Source)) return;
        ColorSchemeBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        _dragDepth++;
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var copy = HasWallpaper(e);
        e.DragEffects = copy ? DragDropEffects.Copy : DragDropEffects.None;
        Group.Classes.Set("drop", copy);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (--_dragDepth <= 0)
        {
            _dragDepth = 0;
            Group.Classes.Set("drop", false);
        }
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _dragDepth = 0;
        Group.Classes.Set("drop", false);
        e.Handled = true;
        if (DataContext is not SettingsViewModel settings) return;
        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return;
        foreach (var item in items)
        {
            if (item is not IStorageFile file || WallpaperBudget.NormalizeExtension(file.Name) is null) continue;
            var picked = await OpenAsync(file);
            if (picked is null) return;
            await using (picked) await settings.Wallpaper.AcceptAsync(picked);
            return;
        }
    }

    private static bool HasWallpaper(DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return false;
        var items = e.DataTransfer.TryGetFiles();
        if (items is null) return false;
        foreach (var item in items)
            if (item is IStorageFile file && WallpaperBudget.NormalizeExtension(file.Name) is not null)
                return true;
        return false;
    }

    private static async Task<PickedFile?> OpenAsync(IStorageFile file)
    {
        var path = file.TryGetLocalPath();
        if (path is not null && File.Exists(path))
        {
            var info = new FileInfo(path);
            return new(file.Name, FileKinds.MimeFromFileName(file.Name), File.OpenRead(path), info.Length);
        }
        var props = await file.GetBasicPropertiesAsync();
        return new(file.Name, FileKinds.MimeFromFileName(file.Name), await file.OpenReadAsync(), (long)(props.Size ?? 0));
    }
}
