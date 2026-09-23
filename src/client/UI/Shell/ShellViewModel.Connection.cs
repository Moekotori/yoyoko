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
    private CancellationTokenSource? _workspaceWork;
    public ConnectionSettingsViewModel Connection { get; }

    private void UnbindSession(InstanceSession session)
    {
        if (!_boundSessions.Remove(session)) return;
        _transport.Detach(session);
    }

    private void OpenConnectionSettings()
    {
        Connection.Bind(SelectedInstance?.Context);
        Settings.Section = SettingsSection.Connection;
        ShowSettings = true;
    }

    private CancellationToken StartWorkspaceWork()
    {
        _workspaceWork?.Dispose();
        _workspaceWork = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        return _workspaceWork.Token;
    }

    private void CancelWorkspaceWork()
    {
        try { _workspaceWork?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void FinishWorkspaceWork()
    {
        _workspaceWork?.Dispose();
        _workspaceWork = null;
    }

    private async Task ConnectWorkspaceAsync()
    {
        if (Connection.IsBusy) return;
        var keepSettings = ShowSettings;
        var work = StartWorkspaceWork();
        Connection.IsBusy = true;
        Connection.Status = _text.Get(TextKey.ConnectingServer);
        try
        {
            var invite = _connection.Resolve(Connection.Address);
            Connection.Address = invite.Address;
            var context = await _connection.ConnectAsync(invite.Address, work,
                allowBootstrapCommunity: !invite.HasCommunityCode,
                preferredUsername: Connection.Username);
            Connection.Username = "";
            var item = TrackInstance(context);
            if (context.Session is { } session)
            {
                BindSession(session);
                if (invite.CommunityCode is { } code) await session.JoinAsync(code, work);
            }
            SelectedInstance = item;
            Connection.Address = context.Descriptor.BaseUrl.AbsoluteUri;
            Connection.Status = "";
            Status = "";
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            Connection.Status = _text.Get(TextKey.Cancelled);
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
        var work = StartWorkspaceWork();
        Connection.IsBusy = true;
        Connection.Status = _text.Get(TextKey.DisconnectingServer);
        try
        {
            var accountId = item.Context.Account?.Key.Id;
            if (item.Context.Session is { } session) UnbindSession(session);
            await _connection.DisconnectAsync(item.Context, work);
            if (accountId is Guid id) ClearAccountDrafts(item.Context.Descriptor.Id.Value, id);
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
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            Connection.Status = _text.Get(TextKey.Cancelled);
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
        FinishWorkspaceWork();
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
