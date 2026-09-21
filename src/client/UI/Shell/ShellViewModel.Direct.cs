using Chat.Localization;
using Chat.UI.Chat;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    public async void MessageMember(MemberProfile member)
    {
        if (member.IsSelf) return;
        if (member.IsFixture)
        {
            Workspace.ShowNotice(_text.Get(TextKey.DirectUnavailable));
            return;
        }
        var session = SelectedInstance?.Context.Session;
        if (session is null)
        {
            Workspace.ShowNotice(_text.Get(TextKey.NeedSignIn));
            return;
        }
        try
        {
            var channel = await session.OpenDirectAsync(member.Id, _lifetime);
            if (SelectedInstance?.Context.Session != session) return;
            RefreshCommunity();
            var item = Channels.FirstOrDefault(entry => entry.Id == channel.Id);
            if (item is not null) SelectOpenChannel.Execute(item);
        }
        catch (Exception exception)
        {
            OnError(exception);
        }
    }
}
