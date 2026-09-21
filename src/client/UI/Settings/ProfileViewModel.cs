using Avalonia.Media;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Settings;

public sealed class ProfileViewModel : ObservableObject
{
    private readonly Func<InstanceSession?> _session;
    private readonly Func<Task<PickedFile?>> _pick;
    private string _username = "";
    private string _displayName = "";
    private string _idText = "";
    private string _status = "";
    private readonly I18n _text;
    private AvatarPlayback? _playback;
    private AvatarPlayback? _bannerPlayback;
    private IBrush _accent = Brushes.Transparent;
    public ProfileViewModel(Func<InstanceSession?> session, Func<Task<PickedFile?>> pick, Action<Exception> onError, I18n text)
    {
        _session = session;
        _pick = pick;
        _text = text;
        void ReportError(Exception error)
        {
            Status = _text.Error(error);
            onError(error);
        }
        Save = new(SaveAsync, ReportError);
        ChangeAvatar = new(ChangeAvatarAsync, ReportError);
        RemoveAvatar = new(RemoveAvatarAsync, ReportError);
        ChangeBanner = new(ChangeBannerAsync, ReportError);
        RemoveBanner = new(RemoveBannerAsync, ReportError);
    }
    public AsyncCommand Save { get; }
    public AsyncCommand ChangeAvatar { get; }
    public AsyncCommand RemoveAvatar { get; }
    public AsyncCommand ChangeBanner { get; }
    public AsyncCommand RemoveBanner { get; }
    public bool IsSignedIn { get; private set; }
    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            Changed();
            Changed(nameof(Initial));
            Changed(nameof(Handle));
            Changed(nameof(HasHandle));
            Changed(nameof(PreviewName));
        }
    }
    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            Changed();
            Changed(nameof(Initial));
            Changed(nameof(PreviewName));
        }
    }
    public string Handle => Username.Length == 0 ? "" : "@" + Username;
    public bool HasHandle => Username.Length > 0;
    public string IdText { get => _idText; private set { if (_idText == value) return; _idText = value; Changed(); } }
    public string PreviewName
    {
        get
        {
            if (!IsSignedIn) return _text.Get(TextKey.NotSignedIn);
            var name = DisplayName.Trim();
            if (name.Length > 0) return name;
            name = Username.Trim();
            return name.Length > 0 ? name : _text.Get(TextKey.NotSignedIn);
        }
    }
    public string Initial
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(DisplayName) ? Username.Trim() : DisplayName.Trim();
            return name.Length == 0 ? "?" : System.Globalization.StringInfo.GetNextTextElement(name);
        }
    }
    public string Status { get => _status; private set { _status = value; Changed(); Changed(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length > 0;
    public AvatarPlayback? Playback
    {
        get => _playback;
        private set { _playback = value; Changed(); Changed(nameof(HasAvatar)); }
    }
    public bool HasAvatar => Playback is not null;
    public AvatarPlayback? BannerPlayback
    {
        get => _bannerPlayback;
        private set { _bannerPlayback = value; Changed(); Changed(nameof(HasBanner)); }
    }
    public bool HasBanner => BannerPlayback is not null;
    public IBrush Accent
    {
        get => _accent;
        private set { if (ReferenceEquals(_accent, value)) return; _accent = value; Changed(); }
    }

    public void Reload()
    {
        var session = _session();
        IsSignedIn = session is not null;
        Changed(nameof(IsSignedIn));
        Changed(nameof(PreviewName));
        if (session is null)
        {
            Username = "";
            DisplayName = "";
            IdText = "";
            Status = "";
            Accent = Brushes.Transparent;
            return;
        }
        Username = session.Me.Username;
        DisplayName = session.Me.DisplayName;
        IdText = session.Me.Id.ToString("D");
        Accent = Banner.AccentFrom(session.Me.Id);
        Status = "";
    }

    public void SetPlayback(AvatarPlayback? playback) => Playback = playback;
    public void SetBannerPlayback(AvatarPlayback? playback) => BannerPlayback = playback;
    public void NotifyText() => Changed(nameof(PreviewName));

    private async Task SaveAsync()
    {
        var session = _session() ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var username = Username.Trim();
        var displayName = DisplayName.Trim();
        if (displayName.Length == 0) displayName = username;
        await session.PatchProfileAsync(username, displayName, null, false, CancellationToken.None);
        Reload();
        Status = _text.Get(TextKey.ProfileSaved);
    }

    private async Task ChangeAvatarAsync()
    {
        var session = _session() ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var file = await _pick();
        if (file is null) return;
        await using (file)
            await session.ChangeAvatarAsync(file, CancellationToken.None);
        Status = _text.Get(TextKey.ProfileSaved);
    }

    private async Task RemoveAvatarAsync()
    {
        var session = _session() ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        await session.PatchProfileAsync(null, null, null, true, CancellationToken.None);
        Status = _text.Get(TextKey.ProfileSaved);
    }

    private async Task ChangeBannerAsync()
    {
        var session = _session() ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        var file = await _pick();
        if (file is null) return;
        await using (file)
            await session.ChangeBannerAsync(file, CancellationToken.None);
        Status = _text.Get(TextKey.ProfileSaved);
    }

    private async Task RemoveBannerAsync()
    {
        var session = _session() ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        await session.PatchProfileAsync(null, null, null, false, CancellationToken.None, null, true);
        Status = _text.Get(TextKey.ProfileSaved);
    }
}
