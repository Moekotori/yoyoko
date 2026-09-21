using Chat.UI.Components;

namespace Chat.UI.Shell;

public sealed partial class ShellViewModel
{
    private bool _profileOpen;
    public ActionCommand OpenProfile { get; }
    public ActionCommand CloseProfile { get; }
    public bool ProfileOpen
    {
        get => _profileOpen;
        private set { if (_profileOpen == value) return; _profileOpen = value; Changed(); }
    }
    public bool IsSelfMuted => SelectedInstance?.Context.Session?.Voice.SelfMute == true;
    public bool IsSelfDeafened => SelectedInstance?.Context.Session?.Voice.SelfDeaf == true;
}
