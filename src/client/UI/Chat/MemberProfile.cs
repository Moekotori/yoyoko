using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;
using Chat.UI.Shell;
using Chat.UI.Workspace;

namespace Chat.UI.Chat;

public sealed class MemberProfile : ObservableObject
{
    private string _name;
    private string _username;
    private bool _isSelf;
    private AvatarPlayback? _playback;
    private IImage? _image;

    public MemberProfile(Guid id, string name, string username, bool isSelf,
        AvatarPlayback? playback = null, IImage? image = null)
    {
        Id = id;
        _name = name;
        _username = username;
        _isSelf = isSelf;
        _playback = playback;
        _image = image;
    }

    public Guid Id { get; }
    public string Name
    {
        get => _name;
        private set
        {
            if (_name == value) return;
            _name = value;
            Changed();
            Changed(nameof(Initial));
            Changed(nameof(MentionToken));
        }
    }
    public string Username
    {
        get => _username;
        private set
        {
            if (_username == value) return;
            _username = value;
            Changed();
            Changed(nameof(Handle));
            Changed(nameof(HasHandle));
            Changed(nameof(MentionToken));
        }
    }
    public bool IsSelf
    {
        get => _isSelf;
        private set { if (_isSelf == value) return; _isSelf = value; Changed(); }
    }
    public AvatarPlayback? Playback
    {
        get => _playback;
        set { if (ReferenceEquals(_playback, value)) return; _playback = value; Changed(); }
    }
    public IImage? Image
    {
        get => _image;
        set { if (ReferenceEquals(_image, value)) return; _image = value; Changed(); }
    }
    public string Handle => Username.Length == 0 ? "" : "@" + Username;
    public bool HasHandle => Username.Length > 0;
    public string IdText => Id.ToString("D");
    public string Initial => Avatar.FromName(Name);
    public string MentionToken => HasHandle ? Handle : Name;

    public void Update(string name, string username, bool isSelf)
    {
        Name = name;
        Username = username;
        IsSelf = isSelf;
    }
}

internal static class MemberGestures
{
    public static void PrepareMenu(ContextMenu menu, MemberProfile? member, bool canModerate,
        Action<string> copy, Action<MemberProfile> mention, Action? profile, Action? moderate,
        CancelEventArgs args)
    {
        if (member is null) { args.Cancel = true; return; }
        ((MenuItem)menu.Items[0]!).Command = new ActionCommand(_ => mention(member));
        var copyName = (MenuItem)menu.Items[1]!;
        copyName.IsVisible = member.HasHandle;
        copyName.Command = new ActionCommand(_ => copy(member.Username));
        ((MenuItem)menu.Items[2]!).Command = new ActionCommand(_ => copy(member.IdText));
        var edit = (MenuItem)menu.Items[3]!;
        edit.IsVisible = member.IsSelf && profile is not null;
        edit.Command = profile is null ? Disabled : new ActionCommand(_ => profile());
        var showMod = canModerate && !member.IsSelf && moderate is not null;
        ((Control)menu.Items[4]!).IsVisible = showMod;
        var kick = (MenuItem)menu.Items[5]!;
        var ban = (MenuItem)menu.Items[6]!;
        kick.IsVisible = showMod;
        ban.IsVisible = showMod;
        ICommand command = moderate is null ? Disabled : new ActionCommand(_ => moderate());
        kick.Command = command;
        ban.Command = command;
    }

    public static MemberProfile? Target(ContextMenu menu) =>
        (menu.PlacementTarget as Control)?.DataContext as MemberProfile
        ?? menu.DataContext as MemberProfile;

    public static async Task CopyAsync(Control owner, string text)
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        var notice = Notice(owner);
        if (clipboard is null)
        {
            notice?.Invoke(I18n.T(TextKey.ClipboardUnavailable));
            return;
        }
        try
        {
            await ClipboardExtensions.SetTextAsync(clipboard, text);
            notice?.Invoke(I18n.T(TextKey.Copied));
        }
        catch (Exception)
        {
            notice?.Invoke(I18n.T(TextKey.ClipboardFailed));
        }
    }

    public static Action<string>? Notice(Control owner) =>
        TopLevel.GetTopLevel(owner)?.DataContext switch
        {
            ShellViewModel shell => shell.Workspace.ShowNotice,
            WorkspaceViewModel workspace => workspace.ShowNotice,
            _ => null
        };

    private static readonly ICommand Disabled = new ActionCommand(_ => { });
}
