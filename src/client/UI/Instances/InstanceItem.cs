using Chat.Core.Instances;
using Chat.UI.Components;

namespace Chat.UI.Instances;

public sealed class InstanceItem(InstanceContext context) : ObservableObject
{
    private Guid? _accountId;
    private Guid? _avatarId;
    private bool _hasUnread;
    private bool _hasMention;
    public AvatarPlayback? Playback { get; private set; }
    public bool HasUnread { get => _hasUnread; set { if (_hasUnread == value) return; _hasUnread = value; Changed(); } }
    public bool HasMention { get => _hasMention; set { if (_hasMention == value) return; _hasMention = value; Changed(); } }
    public InstanceContext Context { get; } = context;
    public string Name => Context.Descriptor.DisplayName;
    public string Host => Context.Descriptor.BaseUrl.Authority;
    public string Initial => Name.Length == 0 ? "·" : System.Globalization.StringInfo.GetNextTextElement(Name);

    public void RefreshIdentity()
    {
        var user = Context.Session?.Me;
        if (_accountId == user?.Id && _avatarId == user?.Avatar?.Id) return;
        ClearAvatar();
        _accountId = user?.Id;
        _avatarId = user?.Avatar?.Id;
    }

    public void SetAvatar(byte[] bytes)
    {
        RefreshIdentity();
        if (_avatarId is null || Playback is not null) return;
        // The rail owns a small static thumbnail independently of the active timeline.
        Playback = AvatarPlayback.Decode(bytes, 64, allowAnimation: false);
        Changed(nameof(Playback));
    }

    public void ClearAvatar()
    {
        var previous = Playback;
        Playback = null;
        Changed(nameof(Playback));
        previous?.Dispose();
    }
}
