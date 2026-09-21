using Chat.Core.Instances;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.UI.Instances;
using Chat.UI.Settings;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private readonly WorkspaceConnection _connection;
    private readonly HashSet<InstanceSession> _boundSessions = [];
    public ConnectionSettingsViewModel Connection { get; }

    private void OpenConnectionSettings()
    {
        Settings.Section = SettingsSection.Connection;
        ShowSettings = true;
    }

    private async Task ConnectWorkspaceAsync()
    {
        if (Connection.IsBusy) return;
        var keepSettings = ShowSettings;
        Connection.IsBusy = true;
        Connection.Status = _text.Get(TextKey.ConnectingServer);
        try
        {
            Connection.Address = _connection.ResolveAddress(Connection.Address);
            var context = await _connection.ConnectAsync(Connection.Address, _lifetime);
            var item = TrackInstance(context);
            if (context.Session is { } session) BindSession(session);
            SelectedInstance = item;
            Connection.Address = context.Descriptor.BaseUrl.AbsoluteUri;
            Connection.Status = "";
            Status = "";
        }
        catch (Exception error)
        {
            // Expose a discovered but signed-out instance for account recovery in settings.
            var context = _instances.Contexts.FirstOrDefault(item =>
                item.Descriptor.BaseUrl.AbsoluteUri.TrimEnd('/') == Connection.Address.Trim().TrimEnd('/'));
            if (context is not null && !IsSignedIn) SelectedInstance = TrackInstance(context);
            Connection.Status = _text.Error(error);
            OnError(error);
        }
        finally
        {
            Connection.IsBusy = false;
            ShowSettings = keepSettings;
            Settings.Profile.Reload();
        }
    }

    private InstanceItem TrackInstance(InstanceContext context)
    {
        var item = Instances.FirstOrDefault(item => item.Context == context);
        if (item is not null) return item;
        var previous = Instances.FirstOrDefault(item => item.Context.Descriptor.Id == context.Descriptor.Id);
        if (previous is not null) Instances.Remove(previous);
        item = new(context);
        Instances.Add(item);
        return item;
    }
}
