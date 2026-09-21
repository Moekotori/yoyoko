using Avalonia.Controls;
using Avalonia.Threading;

namespace Chat.UI.Channels;

public partial class ChannelEditor : UserControl
{
    public ChannelEditor()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => {
            if (DataContext is ChannelEditorViewModel { IsDelete: true }) CancelButton.Focus();
            else { NameInput.Focus(); NameInput.SelectAll(); }
        });
    }
}
