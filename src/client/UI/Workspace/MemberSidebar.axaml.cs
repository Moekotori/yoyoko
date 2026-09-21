using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Chat.Localization;
using Chat.UI.Chat;
using Chat.UI.Localization;

namespace Chat.UI.Workspace;

public partial class MemberSidebar : UserControl
{
    public MemberSidebar() => InitializeComponent();

    private void OnCardOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout flyout) MemberGestures.BindCard(flyout);
    }

    private void OnMemberMenuOpening(object? sender, CancelEventArgs args)
    {
        if (sender is not ContextMenu menu || DataContext is not WorkspaceViewModel workspace)
        {
            args.Cancel = true;
            return;
        }
        var member = MemberGestures.Target(menu);
        MemberGestures.PrepareMenu(menu, member, workspace.IsPreview,
            text => _ = MemberGestures.CopyAsync(this, text),
            workspace.MentionMember,
            _ => workspace.ShowNotice(I18n.T(TextKey.DirectUnavailable)),
            member?.IsSelf == true ? () => workspace.Unavailable.Execute(null) : null,
            () => workspace.ShowNotice(I18n.T(TextKey.NotImplemented)),
            args);
    }
}
