using System.Collections.ObjectModel;
using Chat.Core.Messaging;
using Chat.Localization;
using Chat.UI.Chat;
using Chat.UI.Components;

namespace Chat.UI.Channels;

public sealed class ChannelItem(Guid id, Guid serverId, string name, string kind, string? audioQuality, bool isFixture = false, Guid[]? participants = null) : ObservableObject
{
    public bool IsFixture { get; } = isFixture;
    private ChannelEditorViewModel? _editor;
    public ChannelEditorViewModel? Editor { get => _editor; set { if (ReferenceEquals(_editor, value)) return; _editor = value; Changed(); Changed(nameof(IsEditing)); } }
    public bool IsEditing => Editor is not null;
    private bool _enter;
    public void RequestEnter() => _enter = true;
    public bool ConsumeEnter()
    {
        if (!_enter) return false;
        _enter = false;
        return true;
    }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; Changed(); } }
    private bool _isConnected;
    public bool IsConnected { get => _isConnected; set { if (_isConnected == value) return; _isConnected = value; Changed(); } }
    private bool _hasUnread;
    public bool HasUnread { get => _hasUnread; private set { if (_hasUnread == value) return; _hasUnread = value; Changed(); } }
    private bool _hasMention;
    public bool HasMention { get => _hasMention; private set { if (_hasMention == value) return; _hasMention = value; Changed(); } }
    private bool _isMuted;
    public bool IsMuted { get => _isMuted; private set { if (_isMuted == value) return; _isMuted = value; Changed(); } }
    public bool IsVoice => Kind == "voice";
    public bool IsDirect => Kind == "dm";
    public bool CanChat => Kind is "text" or "voice" or "dm";
    public Guid Id { get; } = id;
    public Guid ServerId { get; } = serverId;
    public Guid[] Participants { get; } = participants ?? [];
    private string _name = name;
    public string Name { get => _name; set { if (_name == value) return; _name = value; Changed(); Changed(nameof(Label)); } }
    public string Kind { get; } = kind;
    public string AudioQuality { get; set; } = audioQuality ?? "studio";
    public string Label => IsDirect ? Name : Kind == "voice" ? Name + " · " + global::Chat.UI.Localization.I18n.T(TextKey.Voice) : "# " + Name;
    public ObservableCollection<VoiceMemberRow> VoiceMembers { get; } = [];
    public bool HasVoiceMembers => VoiceMembers.Count > 0;
    public void Refresh() => Changed(nameof(Label));
    public void ApplyInbox(ChannelInbox inbox, Guid selfId)
    {
        HasUnread = inbox.ShowUnread(selfId);
        HasMention = inbox.ShowMention;
        IsMuted = inbox.IsMuted;
    }
    public void ClearInbox()
    {
        HasUnread = false;
        HasMention = false;
        IsMuted = false;
    }

    public int VoiceCount => VoiceMembers.Count;
    public void SyncMembers(IReadOnlyList<VoiceMemberRow> next)
    {
        for (var i = VoiceMembers.Count - 1; i >= 0; i--)
            if (next.All(item => item.UserId != VoiceMembers[i].UserId))
                VoiceMembers.RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            var item = next[i];
            if (i < VoiceMembers.Count && VoiceMembers[i].UserId == item.UserId)
            {
                VoiceMembers[i].Update(item);
                continue;
            }
            var previous = -1;
            for (var j = 0; j < VoiceMembers.Count; j++)
                if (VoiceMembers[j].UserId == item.UserId) { previous = j; break; }
            if (previous >= 0)
            {
                VoiceMembers[previous].Update(item);
                VoiceMembers.Move(previous, i);
            }
            else VoiceMembers.Insert(i, item);
        }
        Changed(nameof(HasVoiceMembers));
        Changed(nameof(VoiceCount));
    }
}

public sealed class VoiceMemberRow : ObservableObject
{
    private string _name;
    private string _username;
    private bool _muted;
    private bool _deafened;
    private bool _isSelf;
    private bool _speaking;
    private AvatarPlayback? _playback;
    private MemberProfile? _profile;
    public VoiceMemberRow(Guid userId, string name, string username, bool muted, bool deafened, bool isSelf,
        MemberProfile? profile = null, AvatarPlayback? playback = null, bool speaking = false)
    {
        UserId = userId;
        _name = name;
        _username = username;
        _muted = muted;
        _deafened = deafened;
        _isSelf = isSelf;
        _profile = profile;
        _playback = playback;
        _speaking = speaking;
    }
    public Guid UserId { get; }
    public string Name
    {
        get => _name;
        private set { if (_name == value) return; _name = value; Changed(); Changed(nameof(Initial)); }
    }
    public string Username
    {
        get => _username;
        private set { if (_username == value) return; _username = value; Changed(); }
    }
    public bool Muted { get => _muted; private set { if (_muted == value) return; _muted = value; Changed(); } }
    public bool Deafened { get => _deafened; private set { if (_deafened == value) return; _deafened = value; Changed(); } }
    public bool IsSelf { get => _isSelf; private set { if (_isSelf == value) return; _isSelf = value; Changed(); } }
    public bool Speaking { get => _speaking; private set { if (_speaking == value) return; _speaking = value; Changed(); } }
    public bool ShowMute => Muted && !Deafened;
    public AvatarPlayback? Playback
    {
        get => _playback;
        set { if (ReferenceEquals(_playback, value)) return; _playback = value; Changed(); }
    }
    public MemberProfile? Profile
    {
        get => _profile;
        set { if (ReferenceEquals(_profile, value)) return; _profile = value; Changed(); }
    }
    public string Initial => Avatar.FromName(Name);
    public void Update(VoiceMemberRow source)
    {
        Name = source.Name;
        Username = source.Username;
        Muted = source.Muted;
        Deafened = source.Deafened;
        IsSelf = source.IsSelf;
        Speaking = source.Speaking;
        if (source.Playback is not null) Playback = source.Playback;
        if (source.Profile is not null) Profile = source.Profile;
        Changed(nameof(ShowMute));
    }
}
