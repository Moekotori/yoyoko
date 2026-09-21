using Chat.Localization;
using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private string _newChannelName = "";
    private bool _newChannelIsVoice;
    public AsyncCommand CreateChannel { get; private set; } = null!;
    public string NewChannelName { get => _newChannelName; set { _newChannelName = value; Changed(); } }
    public bool NewChannelIsVoice { get => _newChannelIsVoice; set { _newChannelIsVoice = value; Changed(); } }

    private async Task CreateChannelAsync()
    {
        var session = SelectedInstance?.Context.Session;
        var server = ActiveServer;
        if (session is null || server is null)
            throw new InvalidOperationException(_text.Get(TextKey.NeedSignIn));
        if (string.IsNullOrWhiteSpace(NewChannelName))
            throw new InvalidOperationException(_text.Get(TextKey.NewChannelName));
        var channel = await session.CreateChannelAsync(server.Id, NewChannelName,
            NewChannelIsVoice ? "voice" : "text", _lifetime);
        // The user may switch instances while the request is in flight.
        if (SelectedInstance?.Context.Session != session) return;
        NewChannelName = "";
        RefreshCommunity();
        // Creating a voice channel does not enter a media session.
        if (channel.Kind == "text") SelectedChannel = Channels.FirstOrDefault(item => item.Id == channel.Id);
    }
}
