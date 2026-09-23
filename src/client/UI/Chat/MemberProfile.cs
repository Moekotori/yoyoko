using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Chat.UI.Channels;
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
    private bool _isOnline;
    private AvatarPlayback? _playback;
    private AvatarPlayback? _bannerPlayback;
    private IImage? _image;

    public MemberProfile(Guid id, string name, string username, bool isSelf,
        AvatarPlayback? playback = null, IImage? image = null, bool isOnline = false, bool isFixture = false)
    {
        Id = id;
        _name = name;
        _username = username;
        _isSelf = isSelf;
        _isOnline = isOnline;
        IsFixture = isFixture;
        _playback = playback;
        _image = image;
        Accent = Banner.AccentFrom(id);
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
        private set { if (_isSelf == value) return; _isSelf = value; Changed(); Changed(nameof(HasPresenceStatus)); }
    }
    public bool IsOnline
    {
        get => _isOnline;
        set { if (_isOnline == value) return; _isOnline = value; Changed(); Changed(nameof(HasPresenceStatus)); }
    }
    public bool IsFixture { get; }
    public bool HasPresenceStatus => IsSelf || IsOnline || IsFixture;
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
    public AvatarPlayback? BannerPlayback
    {
        get => _bannerPlayback;
        set
        {
            if (ReferenceEquals(_bannerPlayback, value)) return;
            _bannerPlayback = value;
            Changed();
            Changed(nameof(HasBanner));
        }
    }
    public bool HasBanner => BannerPlayback is not null;
    public string Handle => Username.Length == 0 ? "" : "@" + Username;
    public bool HasHandle => Username.Length > 0;
    public string IdText => Id.ToString("D");
    public string Initial => Avatar.FromName(Name);
    public string MentionToken => HasHandle ? Handle : Name;
    public IBrush Accent { get; }

    public void Update(string name, string username, bool isSelf, bool isOnline)
    {
        Name = name;
        Username = username;
        IsSelf = isSelf;
        IsOnline = isOnline;
    }
}

public sealed record MemberSection(string Title, IReadOnlyList<MemberProfile> People);

internal static class MemberGestures
{
    public static void PrepareMenu(ContextMenu menu, MemberProfile? member, bool canModerate,
        Action<string> copy, Action<MemberProfile> mention, Action<MemberProfile>? message, Action? profile, Action? moderate,
        CancelEventArgs args)
    {
        if (member is null) { args.Cancel = true; return; }
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem row || row.Tag is not string tag) continue;
            switch (tag)
            {
                case "message":
                    row.IsVisible = !member.IsSelf && message is not null;
                    row.Command = message is null ? Disabled : new ActionCommand(_ => message(member));
                    break;
                case "mention":
                    row.Command = new ActionCommand(_ => mention(member));
                    break;
                case "copy-name":
                    row.IsVisible = member.HasHandle;
                    row.Command = new ActionCommand(_ => copy(member.Username));
                    break;
                case "copy-id":
                    row.Command = new ActionCommand(_ => copy(member.IdText));
                    break;
                case "profile":
                    row.IsVisible = member.IsSelf && profile is not null;
                    row.Command = profile is null ? Disabled : new ActionCommand(_ => profile());
                    break;
                case "kick":
                case "ban":
                    var showMod = canModerate && !member.IsSelf && moderate is not null;
                    row.IsVisible = showMod;
                    row.Command = moderate is null ? Disabled : new ActionCommand(_ => moderate());
                    break;
            }
        }
        foreach (var item in menu.Items)
            if (item is Separator separator)
                separator.IsVisible = canModerate && !member.IsSelf && moderate is not null;
    }

    public static void BindCard(Flyout flyout)
    {
        if (flyout is not { Target: Control target, Content: Control content }) return;
        content.DataContext = target.DataContext switch
        {
            MessageRow row => row.Profile,
            VoiceMemberRow voice => voice.Profile,
            MemberProfile member => member,
            _ => target.DataContext
        };
    }

    public static void ShowCard(Control target, MemberProfile person)
    {
        var flyout = new Flyout
        {
            Content = new UserCard { DataContext = person },
            Placement = PlacementMode.Top
        };
        flyout.FlyoutPresenterClasses.Add("userCardFlyout");
        flyout.ShowAt(target);
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
