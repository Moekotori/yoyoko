namespace Chat.UI.Channels;

public sealed record ChannelItem(Guid Id, string Name, string Kind)
{
    public string Label => Kind == "voice" ? Name + " · 语音" : "# " + Name;
}
