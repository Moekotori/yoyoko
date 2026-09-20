using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Channels;

public sealed class ChannelItem(Guid id, Guid serverId, string name, string kind, string? audioQuality) : ObservableObject
{
    public Guid Id { get; } = id;
    public Guid ServerId { get; } = serverId;
    public string Name { get; } = name;
    public string Kind { get; } = kind;
    public string AudioQuality { get; set; } = audioQuality ?? "studio";
    public string Label => Kind == "voice" ? Name + " · " + I18n.Presenter.Get(TextKey.Voice) : "# " + Name;
    public void Refresh() => Changed(nameof(Label));
}
