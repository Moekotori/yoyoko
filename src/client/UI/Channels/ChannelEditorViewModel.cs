using System.Text;
using Chat.Core.Sessions;
using Chat.Localization;
using Chat.UI.Components;
using Chat.UI.Localization;

namespace Chat.UI.Channels;

public enum ChannelEditMode { Create, Rename, Delete }

public sealed class ChannelEditorViewModel : ObservableObject
{
    private string _name;
    private string _error = "";
    private bool _busy;
    public ChannelEditorViewModel(InstanceSession session, Guid serverId, ChannelItem? channel,
        bool voice, ChannelEditMode mode, I18n text, Action<Guid?> completed, Action cancel, CancellationToken lifetime)
    {
        Mode = mode;
        ChannelId = mode == ChannelEditMode.Create ? null : channel?.Id;
        IsVoice = voice;
        _name = mode == ChannelEditMode.Create ? "" : channel?.Name ?? "";
        Title = text.Get(mode switch
        {
            ChannelEditMode.Rename => TextKey.RenameChannel,
            ChannelEditMode.Delete => TextKey.DeleteChannel,
            _ => voice ? TextKey.CreateVoiceChannel : TextKey.CreateTextChannel
        });
        IsDelete = mode == ChannelEditMode.Delete;
        Confirmation = IsDelete ? text.Get(TextKey.DeleteChannelConfirm, _name) : "";
        SubmitLabel = text.Get(IsDelete ? TextKey.DeleteChannel : mode == ChannelEditMode.Create ? TextKey.CreateChannel : TextKey.ChannelSave);
        Cancel = new(_ => { if (!IsBusy) cancel(); });
        Submit = new(async () =>
        {
            Error = "";
            if (!IsDelete && (string.IsNullOrWhiteSpace(Name) || Encoding.UTF8.GetByteCount(Name.Trim()) > 100))
            { Error = text.Get(TextKey.ChannelNameInvalid); return; }
            IsBusy = true;
            try
            {
                Guid? created = null;
                if (mode == ChannelEditMode.Create)
                    created = (await session.CreateChannelAsync(serverId, Name, voice ? "voice" : "text", lifetime)).Id;
                else if (mode == ChannelEditMode.Rename)
                    await session.RenameChannelAsync(channel!.Id, Name, lifetime);
                else await session.DeleteChannelAsync(channel!.Id, lifetime);
                completed(created);
            }
            finally { IsBusy = false; }
        }, exception => Error = text.Error(exception));
    }
    public string Name { get => _name; set { _name = value; Changed(); } }
    public string Error { get => _error; private set { _error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); } }
    public ChannelEditMode Mode { get; }
    public Guid? ChannelId { get; }
    public bool IsVoice { get; }
    public bool IsDelete { get; }
    public string Title { get; }
    public string Confirmation { get; }
    public string SubmitLabel { get; }
    public AsyncCommand Submit { get; }
    public ActionCommand Cancel { get; }
}
