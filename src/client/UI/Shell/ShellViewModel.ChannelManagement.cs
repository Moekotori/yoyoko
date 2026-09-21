using Chat.UI.Channels;
using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private ChannelEditorViewModel? _channelEditor;
    public ChannelEditorViewModel? ChannelEditor { get => _channelEditor; private set { _channelEditor = value; Changed(); Changed(nameof(IsChannelEditorOpen)); } }
    public bool IsChannelEditorOpen => ChannelEditor is not null;
    public ActionCommand AddTextChannel { get; private set; } = null!;
    public ActionCommand AddVoiceChannel { get; private set; } = null!;
    public bool CanManageChannel(ChannelItem channel) => SelectedInstance?.Context.Session?.Servers
        .Any(server => server.Id == channel.ServerId && server.OwnerId == SelectedInstance.Context.Account?.Key.Id) == true;

    private void InitializeChannelManagement()
    {
        AddTextChannel = new(_ => EditChannel(null, false, ChannelEditMode.Create));
        AddVoiceChannel = new(_ => EditChannel(null, true, ChannelEditMode.Create));
    }
    public void EditChannel(ChannelItem? channel, bool voice, ChannelEditMode mode)
    {
        var session = SelectedInstance?.Context.Session;
        var serverId = channel?.ServerId ?? ActiveServer?.Id;
        if (session is null || serverId is null || (channel is null ? !CanModerate : !CanManageChannel(channel))) return;
        ChannelEditor = new(session, serverId.Value, channel, voice, mode, _text, created => {
            ChannelEditor = null;
            if (SelectedInstance?.Context.Session != session) return;
            RefreshCommunity();
            if (created is Guid id) SelectedChannel = Channels.FirstOrDefault(item => item.Id == id);
        }, () => ChannelEditor = null, _lifetime);
    }
}
