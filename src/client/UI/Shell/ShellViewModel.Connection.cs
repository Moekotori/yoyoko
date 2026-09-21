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
        Connection.Bind(SelectedInstance?.Context);
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
            var invite = _connection.Resolve(Connection.Address);
            var context = await _connection.ConnectAsync(invite.Address, _lifetime,
                allowBootstrapCommunity: !invite.HasCommunityCode);
            var item = TrackInstance(context);
            if (context.Session is { } session)
            {
                BindSession(session);
                if (invite.CommunityCode is { } code) await session.JoinAsync(code, _lifetime);
            }
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
            FinishConnection(keepSettings);
        }
    }

    private async Task DisconnectWorkspaceAsync()
    {
        if (Connection.IsBusy) return;
        var item = SelectedInstance;
        if (item is null) return;
        var keepSettings = ShowSettings;
        Connection.IsBusy = true;
        Connection.Status = _text.Get(TextKey.DisconnectingServer);
        try
        {
            if (item.Context.Session is { } session) _boundSessions.Remove(session);
            await _connection.DisconnectAsync(item.Context, _lifetime);
            ClearAccountDrafts();
            DetachTimeline();
            foreach (var pending in PendingFiles.ToList())
                pending.File.Content.Dispose();
            PendingFiles.Clear();
            NotifyPending();
            Channels.Clear();
            Messages.Clear();
            SelectedChannel = null;
            ClearPlaybacks();
            NotifySession();
            Connection.Status = "";
            Status = "";
        }
        catch (Exception error)
        {
            Connection.Status = _text.Error(error);
            OnError(error);
        }
        finally
        {
            FinishConnection(keepSettings);
        }
    }

    private async Task<int> ProbeLatencyAsync(CancellationToken token)
    {
        var url = SelectedInstance?.Context.Descriptor.BaseUrl
            ?? throw new InvalidOperationException();
        var elapsed = await _discovery.ProbeAsync(url, token);
        return (int)Math.Round(Math.Clamp(elapsed.TotalMilliseconds, 0, 99_999));
    }

    private void FinishConnection(bool keepSettings)
    {
        Connection.IsBusy = false;
        Connection.Bind(SelectedInstance?.Context);
        ShowSettings = keepSettings;
        Connection.SetWatching(keepSettings && Settings.Section == SettingsSection.Connection);
        Settings.Profile.Reload();
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
