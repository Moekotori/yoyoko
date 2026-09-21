using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Chat.Localization;
using Chat.UI.Localization;
using Chat.UI.Shell;

namespace Chat.UI.Chat;

public partial class MemberSidebar : UserControl
{
    public MemberSidebar() => InitializeComponent();

    private void OnCardOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Target: Control target, Content: Control content })
            content.DataContext = target.DataContext;
    }

    private void OnMemberMenuOpening(object? sender, CancelEventArgs args)
    {
        if (sender is not ContextMenu menu || DataContext is not ShellViewModel shell)
        {
            args.Cancel = true;
            return;
        }
        var member = MemberGestures.Target(menu);
        MemberGestures.PrepareMenu(menu, member, shell.CanModerate,
            text => _ = MemberGestures.CopyAsync(this, text),
            shell.MentionMember,
            member?.IsSelf == true ? () => shell.OpenProfile.Execute(null) : null,
            () => shell.Workspace.ShowNotice(I18n.T(TextKey.NotImplemented)),
            args);
    }
}
