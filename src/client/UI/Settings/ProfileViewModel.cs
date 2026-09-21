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
    }
    public AsyncCommand Save { get; }
    public AsyncCommand ChangeAvatar { get; }
    public AsyncCommand RemoveAvatar { get; }
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
        }
    }
    public string DisplayName { get => _displayName; set { _displayName = value; Changed(); Changed(nameof(Initial)); } }
    public string Handle => Username.Length == 0 ? "" : "@" + Username;
    public bool HasHandle => Username.Length > 0;
    public string IdText { get => _idText; private set { if (_idText == value) return; _idText = value; Changed(); } }
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

    public void Reload()
    {
        var session = _session();
        IsSignedIn = session is not null;
        Changed(nameof(IsSignedIn));
        if (session is null)
        {
            Username = "";
            DisplayName = "";
            IdText = "";
            Status = "";
            return;
        }
        Username = session.Me.Username;
        DisplayName = session.Me.DisplayName;
        IdText = session.Me.Id.ToString("D");
        Status = "";
    }

    public void SetPlayback(AvatarPlayback? playback) => Playback = playback;

    private async Task SaveAsync()
    {
        var session = _session() ?? throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        await session.PatchProfileAsync(Username.Trim(), DisplayName.Trim(), null, false, CancellationToken.None);
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
}
