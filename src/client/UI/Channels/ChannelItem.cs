using Chat.Localization;
using Chat.UI.Components;

namespace Chat.UI.Channels;

public sealed class ChannelItem(Guid id, Guid serverId, string name, string kind, string? audioQuality) : ObservableObject
{
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; Changed(); } }
    public bool IsVoice => Kind == "voice";
    public Guid Id { get; } = id;
    public Guid ServerId { get; } = serverId;
    private string _name = name;
    public string Name { get => _name; set { if (_name == value) return; _name = value; Changed(); Changed(nameof(Label)); } }
    public string Kind { get; } = kind;
    public string AudioQuality { get; set; } = audioQuality ?? "studio";
    public string Label => Kind == "voice" ? Name + " · " + global::Chat.UI.Localization.I18n.T(TextKey.Voice) : "# " + Name;
    public void Refresh() => Changed(nameof(Label));
}
