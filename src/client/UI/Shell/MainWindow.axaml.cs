using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Chat.Core.Sessions;

namespace Chat.UI.Shell;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is ShellViewModel shell)
            shell.PickImages = PickImagesAsync;
    }

    private async Task<IReadOnlyList<PickedImage>> PickImagesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "发送图片",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });
        var result = new List<PickedImage>();
        foreach (var file in files.Take(4))
        {
            var props = await file.GetBasicPropertiesAsync();
            var mime = file.ContentType ?? "application/octet-stream";
            var stream = await file.OpenReadAsync();
            result.Add(new(file.Name, mime, stream, (long)(props.Size ?? 0)));
        }
        return result;
    }
}
