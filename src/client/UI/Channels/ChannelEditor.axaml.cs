using Avalonia.Controls;
using Avalonia.Threading;

namespace Chat.UI.Channels;

public partial class ChannelEditor : UserControl
{
    public ChannelEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusInput();
        DataContextChanged += (_, _) => FocusInput();
    }
    private void FocusInput() => Dispatcher.UIThread.Post(() =>
    {
        if (!IsLoaded || DataContext is not ChannelEditorViewModel editor) return;
        if (editor.IsDelete) CancelButton.Focus();
        else { NameInput.Focus(); NameInput.SelectAll(); }
    });
}
