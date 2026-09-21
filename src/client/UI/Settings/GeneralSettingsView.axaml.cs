using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Chat.UI.Settings;

public partial class GeneralSettingsView : UserControl
{
    private int _dragDepth;
    public GeneralSettingsView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        _dragDepth++;
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var json = HasJson(e);
        e.DragEffects = json ? DragDropEffects.Copy : DragDropEffects.None;
        if (DataContext is SettingsViewModel settings) settings.LanguagePackDropActive = json;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (--_dragDepth <= 0)
        {
            _dragDepth = 0;
            if (DataContext is SettingsViewModel settings) settings.LanguagePackDropActive = false;
        }
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _dragDepth = 0;
        if (DataContext is not SettingsViewModel settings) return;
        settings.LanguagePackDropActive = false;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;
        var paths = new List<string>();
        foreach (var item in files)
            if (item.TryGetLocalPath() is { Length: > 0 } path && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        if (paths.Count > 0) settings.ImportDropped(paths);
        e.Handled = true;
    }

    private static bool HasJson(DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return false;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return false;
        foreach (var item in files)
            if (item.TryGetLocalPath() is { Length: > 0 } path && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
