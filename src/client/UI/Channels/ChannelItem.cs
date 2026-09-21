using System.Collections.ObjectModel;
using Chat.Core.Messaging;
using Chat.Localization;
using Chat.UI.Components;

namespace Chat.UI.Channels;

public sealed class ChannelItem(Guid id, Guid serverId, string name, string kind, string? audioQuality) : ObservableObject
{
    private ChannelEditorViewModel? _editor;
    public ChannelEditorViewModel? Editor { get => _editor; set { if (ReferenceEquals(_editor, value)) return; _editor = value; Changed(); Changed(nameof(IsEditing)); } }
    public bool IsEditing => Editor is not null;
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set { if (_isSelected == value) return; _isSelected = value; Changed(); } }
    private bool _isConnected;
    public bool IsConnected { get => _isConnected; set { if (_isConnected == value) return; _isConnected = value; Changed(); } }
    private bool _hasUnread;
    public bool HasUnread { get => _hasUnread; private set { if (_hasUnread == value) return; _hasUnread = value; Changed(); } }
    private bool _hasMention;
    public bool HasMention { get => _hasMention; private set { if (_hasMention == value) return; _hasMention = value; Changed(); } }
    private bool _isMuted;
    public bool IsMuted { get => _isMuted; private set { if (_isMuted == value) return; _isMuted = value; Changed(); } }
    public bool IsVoice => Kind == "voice";
    public Guid Id { get; } = id;
    public Guid ServerId { get; } = serverId;
    private string _name = name;
    public string Name { get => _name; set { if (_name == value) return; _name = value; Changed(); Changed(nameof(Label)); } }
    public string Kind { get; } = kind;
    public string AudioQuality { get; set; } = audioQuality ?? "studio";
    public string Label => Kind == "voice" ? Name + " · " + global::Chat.UI.Localization.I18n.T(TextKey.Voice) : "# " + Name;
    public ObservableCollection<VoiceMemberRow> VoiceMembers { get; } = [];
    public bool HasVoiceMembers => VoiceMembers.Count > 0;
    public void Refresh() => Changed(nameof(Label));
    public void ApplyInbox(ChannelInbox inbox, Guid selfId)
    {
        HasUnread = !IsVoice && inbox.ShowUnread(selfId);
        HasMention = !IsVoice && inbox.ShowMention;
        IsMuted = inbox.IsMuted;
    }
    public void ClearInbox()
    {
        HasUnread = false;
        HasMention = false;
        IsMuted = false;
    }

    public void SyncMembers(IReadOnlyList<(Guid Id, string Name, bool Muted, bool Deafened)> next)
    {
        for (var i = VoiceMembers.Count - 1; i >= 0; i--)
            if (next.All(item => item.Id != VoiceMembers[i].UserId))
                VoiceMembers.RemoveAt(i);
        for (var i = 0; i < next.Count; i++)
        {
            var item = next[i];
            if (i < VoiceMembers.Count && VoiceMembers[i].UserId == item.Id)
            {
                VoiceMembers[i].Update(item.Name, item.Muted, item.Deafened);
                continue;
            }
            var previous = -1;
            for (var j = 0; j < VoiceMembers.Count; j++)
                if (VoiceMembers[j].UserId == item.Id) { previous = j; break; }
            if (previous >= 0)
            {
                VoiceMembers[previous].Update(item.Name, item.Muted, item.Deafened);
                VoiceMembers.Move(previous, i);
            }
            else VoiceMembers.Insert(i, new VoiceMemberRow(item.Id, item.Name, item.Muted, item.Deafened));
        }
        Changed(nameof(HasVoiceMembers));
    }
}

public sealed class VoiceMemberRow : ObservableObject
{
    private string _name;
    private bool _muted;
    private bool _deafened;
    public VoiceMemberRow(Guid userId, string name, bool muted, bool deafened)
    {
        UserId = userId;
        _name = name;
        _muted = muted;
        _deafened = deafened;
    }
    public Guid UserId { get; }
    public string Name
    {
        get => _name;
        private set { if (_name == value) return; _name = value; Changed(); Changed(nameof(Initial)); }
    }
    public bool Muted { get => _muted; private set { if (_muted == value) return; _muted = value; Changed(); } }
    public bool Deafened { get => _deafened; private set { if (_deafened == value) return; _deafened = value; Changed(); } }
    public string Initial => Avatar.FromName(Name);
    public void Update(string name, bool muted, bool deafened)
    {
        Name = name;
        Muted = muted;
        Deafened = deafened;
    }
}
