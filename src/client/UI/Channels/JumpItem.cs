using Chat.UI.Chat;
using Chat.UI.Components;

namespace Chat.UI.Channels;

public sealed class JumpItem : ObservableObject
{
    private bool _isActive;

    public JumpItem(ChannelItem channel, bool recent)
    {
        Channel = channel;
        IsRecent = recent;
    }

    public JumpItem(MemberProfile person)
    {
        Person = person;
    }

    public ChannelItem? Channel { get; }
    public MemberProfile? Person { get; }
    public bool IsPerson => Person is not null;
    public bool IsVoice => Channel?.IsVoice == true;
    public bool IsText => Channel is { IsVoice: false };
    public bool IsRecent { get; }
    public string Name => Person?.Name ?? Channel?.Name ?? "";
    public string Handle => Person?.Handle ?? "";
    public bool HasHandle => Handle.Length > 0;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive == value) return; _isActive = value; Changed(); }
    }
}
