using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Chat.Localization;
using FileKinds = global::Chat.Core.Messaging.FileKinds;
using PickedFile = global::Chat.Core.Sessions.PickedFile;

namespace Chat.UI.Shell;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is ShellViewModel shell)
        {
            shell.PickFiles = PickFilesAsync;
            shell.PickAvatar = PickAvatarAsync;
            shell.OpenSaveStream = OpenSaveStreamAsync;
        }
    }

    private async Task<IReadOnlyList<PickedFile>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = global::Chat.UI.Localization.I18n.Presenter.Get(TextKey.PickFiles),
            AllowMultiple = true
        });
        var result = new List<PickedFile>();
        foreach (var file in files.Take(4))
        {
            var props = await file.GetBasicPropertiesAsync();
            var stream = await file.OpenReadAsync();
            result.Add(new(file.Name, FileKinds.MimeFromFileName(file.Name), stream, (long)(props.Size ?? 0)));
        }
        return result;
    }

    private async Task<PickedFile?> PickAvatarAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = global::Chat.UI.Localization.I18n.Presenter.Get(TextKey.PickAvatar),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(global::Chat.UI.Localization.I18n.Presenter.Get(TextKey.Image))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"]
                }
            ]
        });
        var file = files.Count == 0 ? null : files[0];
        if (file is null) return null;
        var props = await file.GetBasicPropertiesAsync();
        var stream = await file.OpenReadAsync();
        return new PickedFile(file.Name, FileKinds.MimeFromFileName(file.Name), stream, (long)(props.Size ?? 0));
    }

    private async Task<Stream?> OpenSaveStreamAsync(string fileName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = global::Chat.UI.Localization.I18n.Presenter.Get(TextKey.SaveFile),
            SuggestedFileName = fileName
        });
        return file is null ? null : await file.OpenWriteAsync();
    }
}
