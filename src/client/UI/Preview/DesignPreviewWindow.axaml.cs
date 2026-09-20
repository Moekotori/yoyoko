using Avalonia.Controls;
using Chat.Localization;
using Chat.UI.Localization;
using Chat.UI.Workspace;

namespace Chat.UI.Preview;

// An explicitly requested visual fixture. No sessions, HTTP, cache or media are constructed.
public partial class DesignPreviewWindow : Window
{
    public DesignPreviewWindow() => InitializeComponent();

    public DesignPreviewWindow(string productName, I18n text) : this()
    {
        var workspace = new WorkspaceViewModel(true, text);
        DataContext = workspace;
        Title = text.Get(TextKey.WindowTitlePreview, productName);
        Closed += (_, _) => workspace.Dispose();
    }
}
