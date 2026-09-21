using Chat.UI.Components;

namespace Chat.UI.Channels;

public sealed class JumpItem(ChannelItem channel) : ObservableObject
{
    private bool _isActive;
    public ChannelItem Channel { get; } = channel;
    public string Name => Channel.Name;
    public bool IsVoice => Channel.IsVoice;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive == value) return; _isActive = value; Changed(); }
    }
}
