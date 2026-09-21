using Chat.UI.Channels;
using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private ChannelEditorViewModel? _channelEditor;
    public ChannelEditorViewModel? ChannelEditor
    {
        get => _channelEditor;
        private set
        {
            _channelEditor = value;
            Changed(); Changed(nameof(IsChannelEditorOpen));
            Changed(nameof(NewTextChannelEditor)); Changed(nameof(NewVoiceChannelEditor));
            Changed(nameof(HasNewTextChannelEditor)); Changed(nameof(HasNewVoiceChannelEditor));
            RefreshChannelEditor();
        }
    }
    public ChannelEditorViewModel? NewTextChannelEditor => ChannelEditor is { Mode: ChannelEditMode.Create, IsVoice: false } editor ? editor : null;
    public ChannelEditorViewModel? NewVoiceChannelEditor => ChannelEditor is { Mode: ChannelEditMode.Create, IsVoice: true } editor ? editor : null;

    public bool HasNewTextChannelEditor => NewTextChannelEditor is not null;
    public bool HasNewVoiceChannelEditor => NewVoiceChannelEditor is not null;

    private void RefreshChannelEditor()
    {
        foreach (var item in Channels)
            item.Editor = ChannelEditor?.ChannelId == item.Id ? ChannelEditor : null;
    }
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
        if (ChannelEditor?.IsBusy == true) return;
        var session = SelectedInstance?.Context.Session;
        var serverId = channel?.ServerId ?? ActiveServer?.Id;
        if (session is null || serverId is null || (channel is null ? !CanModerate : !CanManageChannel(channel))) return;
        ChannelEditorViewModel? editor = null;
        editor = new(session, serverId.Value, channel, voice, mode, _text, created =>
        {
            if (!ReferenceEquals(ChannelEditor, editor)) return;
            ChannelEditor = null;
            if (SelectedInstance?.Context.Session != session) return;
            RefreshCommunity();
            if (created is Guid id) SelectedChannel = Channels.FirstOrDefault(item => item.Id == id);
        }, () => ChannelEditor = null, _lifetime);
        if (voice) VoiceExpanded = true; else TextExpanded = true;
        ChannelEditor = editor;
    }
}
