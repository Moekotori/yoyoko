using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Chat.UI.Components;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new(name));
}
