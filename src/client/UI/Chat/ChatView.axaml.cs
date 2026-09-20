using Avalonia.Controls;
using Chat.UI.Shell;

namespace Chat.UI.Chat;

public partial class ChatView : UserControl
{
    public ChatView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ShellViewModel shell)
            shell.ScrollToLatest = ScrollToEnd;
    }

    private void ScrollToEnd()
    {
        if (Messages.ItemCount == 0) return;
        Messages.ScrollIntoView(Messages.ItemCount - 1);
    }
}
