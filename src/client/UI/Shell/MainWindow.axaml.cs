using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Chat.Localization;
using Chat.UI.Chat;
using Chat.UI.Settings;
using Chat.UI.Shortcuts;
using FileKinds = global::Chat.Core.Messaging.FileKinds;
using PickedFile = global::Chat.Core.Sessions.PickedFile;

namespace Chat.UI.Shell;

public partial class MainWindow : Window
{
    private global::Chat.UI.Resources.WindowResourceGovernor? _resources;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnWindowTextInput, RoutingStrategies.Tunnel);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _resources ??= new(this, budget =>
        {
            if (DataContext is ShellViewModel model) model.ApplyVisualBudget(budget);
        });
        InitializeUltraLight();
        if (DataContext is ShellViewModel shell)
        {
            shell.PickFiles = PickFilesAsync;
            shell.PickAvatar = PickAvatarAsync;
            shell.OpenSaveStream = OpenSaveStreamAsync;
            shell.Wallpaper.PickFile = PickWallpaperAsync;
            shell.Settings.PickLanguagePackPaths = PickLanguagePacksAsync;
            shell.Settings.SaveLanguageTemplate = SaveLanguageTemplateAsync;
            shell.ActivateRequested -= OnActivateRequested;
            shell.ActivateRequested += OnActivateRequested;
            BindOsTransport(shell);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is ShellViewModel shell) BindOsTransport(shell);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.ActivateRequested -= OnActivateRequested;
        base.OnClosed(e);
    }

    private void BindOsTransport(ShellViewModel shell)
        => shell.BindOsTransport(TryGetPlatformHandle()?.Handle ?? 0);

    private void OnActivateRequested() => Dispatcher.UIThread.Post(() =>
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    });

    private async Task<IReadOnlyList<PickedFile>> PickFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = global::Chat.UI.Localization.I18n.T(TextKey.PickFiles),
            AllowMultiple = true
        });
        var limit = (DataContext as ShellViewModel)?.AttachmentLimit ?? global::Chat.Protocol.ProtocolVersion.MaxAttachmentsPerMessage;
        return await IncomingFiles.OpenAsync(files, limit);
    }

    private async Task<PickedFile?> PickAvatarAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = global::Chat.UI.Localization.I18n.T(TextKey.PickAvatar),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(global::Chat.UI.Localization.I18n.T(TextKey.Image))
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

    private async Task<PickedFile?> PickWallpaperAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = global::Chat.UI.Localization.I18n.T(TextKey.PickBackground),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(global::Chat.UI.Localization.I18n.T(TextKey.CustomBackground))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.mp4", "*.webm", "*.mov", "*.mkv", "*.m4v"],
                    MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/webp", "video/mp4", "video/webm", "video/quicktime", "video/x-matroska"]
                }
            ]
        });
        var file = files.Count == 0 ? null : files[0];
        if (file is null) return null;
        var props = await file.GetBasicPropertiesAsync();
        var stream = await file.OpenReadAsync();
        return new PickedFile(file.Name, FileKinds.MimeFromFileName(file.Name), stream, (long)(props.Size ?? 0));
    }

    private async Task<IReadOnlyList<string>> PickLanguagePacksAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = global::Chat.UI.Localization.I18n.T(TextKey.ImportLanguagePack),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(global::Chat.UI.Localization.I18n.T(TextKey.LanguagePacks))
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"]
                }
            ]
        });
        var paths = new List<string>();
        foreach (var file in files)
            if (file.TryGetLocalPath() is { Length: > 0 } path) paths.Add(path);
        return paths;
    }

    private async Task SaveLanguageTemplateAsync(string fileName, string json)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = global::Chat.UI.Localization.I18n.T(TextKey.ExportLanguageTemplate),
            SuggestedFileName = fileName,
            FileTypeChoices =
            [
                new FilePickerFileType(global::Chat.UI.Localization.I18n.T(TextKey.LanguagePacks))
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"]
                }
            ]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
    }

    private async Task<Stream?> OpenSaveStreamAsync(string fileName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = global::Chat.UI.Localization.I18n.T(TextKey.SaveFile),
            SuggestedFileName = fileName
        });
        return file is null ? null : await file.OpenWriteAsync();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not ShellViewModel shell) return;
        if (shell.Settings.Shortcuts.IsRecording) return;
        if (shell.ShowAddInstance)
        {
            if (e.Key == Key.Escape)
            {
                shell.CloseAddInstance.Execute(null);
                e.Handled = true;
            }
            return;
        }
        if (shell.ShowSettings && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None
            && IsWithinSettings(e.Source))
            return;
        var pressed = KeyChord.FromKeyEvent(e);
        var action = pressed is { } chord ? ShortcutScheme.Match(chord, shell.Settings.Shortcuts.Overrides) : null;
        if (shell.ShowVoiceChannelSettings)
        {
            if (e.Key == Key.Escape) { shell.VoiceChannelSettings?.Close.Execute(null); e.Handled = true; }
            return;
        }
        if (shell.ProfileOpen)
        {
            if (e.Key == Key.Escape) { shell.CloseProfile.Execute(null); e.Handled = true; }
            else if (action is ShortcutAction.Jump) { shell.CloseProfile.Execute(null); shell.OpenJump(); e.Handled = true; }
            else if (action is ShortcutAction.Settings) { shell.CloseProfile.Execute(null); shell.OpenSettings.Execute(null); e.Handled = true; }
            return;
        }
        if (shell.IsChannelEditorOpen)
        {
            if (e.Key == Key.Escape) { shell.DismissPresentation.Execute(null); e.Handled = true; }
            return;
        }

        if (shell.SwitcherOpen)
        {
            if (e.Key == Key.Escape || action is ShortcutAction.CloseTab) { shell.CloseJump(); e.Handled = true; }
            else if (e.Key == Key.Enter && action is null) { shell.ConfirmJump(null); e.Handled = true; }
            else if (e.Key is Key.Up or Key.Down && action is null)
            {
                shell.MoveJump(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
            }
            else if (action is ShortcutAction.Search or ShortcutAction.Jump or ShortcutAction.Settings)
            {
                if (shell.ExecuteShortcut(action.Value)) e.Handled = true;
            }
            return;
        }

        if (action is { } matched && shell.ExecuteShortcut(matched))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            shell.DismissPresentation.Execute(null);
            e.Handled = true;
            return;
        }

        var mod = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (!mod || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return;
        if (e.Key is >= Key.D1 and <= Key.D9) { shell.SelectOpenTab(e.Key - Key.D1); e.Handled = true; }
        else if (e.Key is >= Key.NumPad1 and <= Key.NumPad9) { shell.SelectOpenTab(e.Key - Key.NumPad1); e.Handled = true; }
    }

    private void OnWindowTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Handled || DataContext is not ShellViewModel shell) return;
        if (!shell.ShowChat || shell.ShowAddInstance || shell.ProfileOpen || shell.SwitcherOpen || shell.SearchOpen || shell.IsChannelEditorOpen) return;
        if (IsEditable(e.Source)) return;
        shell.AppendDraft(e.Text ?? "");
        e.Handled = true;
    }

    private static bool IsEditable(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is TextBox or ComboBox) return true;
            if (current is FlyoutPresenter) return true;
        }
        return false;
    }

    private static bool IsWithinSettings(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
            if (current is SettingsView) return true;
        return false;
    }
}
