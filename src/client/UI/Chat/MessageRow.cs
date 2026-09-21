using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Chat.Core.Messaging;
using Chat.Core.Sessions;
using Chat.Protocol;
using Chat.UI.Components;

namespace Chat.UI.Chat;

public sealed class PendingFileItem
{
    public PendingFileItem(PickedFile file, Action<PendingFileItem> remove)
    {
        File = file;
        Label = file.FileName + " · " + FileKinds.SizeLabel(file.Size);
        Remove = new ActionCommand(_ => remove(this));
    }
    public PickedFile File { get; }
    public string Label { get; }
    public ICommand Remove { get; }
}

public sealed class MessageRow : ObservableObject
{
    private readonly Func<AttachmentDto, Task> _save = _ => Task.CompletedTask;
    private string _author = "";
    private AvatarPlayback? _playback;
    private MemberProfile? _profile;
    private bool _continuation;
    private bool _own;
    public MessageRow(TimelineItem item, string author, Func<AttachmentDto, Task> save,
        Func<TimelineItem, Task> retry, Action<Exception> onError, bool own)
    {
        Item = item;
        _author = author;
        _save = save;
        _own = own;
        Retry = new AsyncCommand(() => retry(Item), onError);
        Update();
    }

    private MessageRow()
    {
        IsUnreadDivider = true;
        Item = null!;
        Retry = new ActionCommand(_ => { });
    }

    public static MessageRow UnreadDivider() => new();
    public ICommand Retry { get; }
    public TimelineItem Item { get; private set; }
    public bool IsUnreadDivider { get; }
    public Guid Id => IsUnreadDivider ? Guid.Empty : Item.Message.Id;
    public string Author { get => _author; private set { _author = value; Changed(); Changed(nameof(Initial)); } }
    public string Initial => Avatar.FromName(_author);
    public void SetAuthor(string author, bool own)
    {
        Author = author;
        IsOwn = own;
    }
    public AvatarPlayback? Playback
    {
        get => _playback;
        set { _playback = value; Changed(); }
    }
    public MemberProfile? Profile
    {
        get => _profile;
        set { if (ReferenceEquals(_profile, value)) return; _profile = value; Changed(); }
    }
    public string Content => IsUnreadDivider ? "" : Item.Message.Content ?? "";
    public bool HasText => !IsUnreadDivider && !string.IsNullOrEmpty(Content);
    public string Time => IsUnreadDivider ? "" : Item.Message.CreatedAt.ToLocalTime().ToString("HH:mm");
    public bool IsOwn { get => _own; private set { if (_own == value) return; _own = value; Changed(); Changed(nameof(CanEdit)); } }
    public bool CanEdit => IsOwn && !IsUnreadDivider && !IsFailed && Item.Status == SendStatus.Sent;
    public bool IsEdited => !IsUnreadDivider && Item.Message.EditedAt is not null;
    public bool IsContinuation
    {
        get => _continuation;
        private set
        {
            if (_continuation == value) return;
            _continuation = value;
            Changed();
            Changed(nameof(ShowHeader));
        }
    }
    public bool ShowHeader => !IsContinuation && !IsUnreadDivider;
    public bool IsPending => !IsUnreadDivider && Item.Status == SendStatus.Sending;
    public bool IsFailed => !IsUnreadDivider && Item.Status == SendStatus.Failed;
    public bool ShowSendState => IsPending || IsFailed;
    public void SetContinuation(bool value) => IsContinuation = value;
    public ObservableCollection<AttachmentRow> Files { get; } = [];
    public bool HasFiles => Files.Count > 0;
    public void Update(TimelineItem? item = null)
    {
        if (IsUnreadDivider) return;
        if (item is not null) Item = item;
        var next = Item.Message.Attachments ?? [];
        for (var i = Files.Count - 1; i >= 0; i--)
            if (!next.Any(dto => Same(Files[i].Dto, dto)))
                Files.RemoveAt(i);
        foreach (var dto in next)
        {
            var existing = Files.FirstOrDefault(file => Same(file.Dto, dto));
            if (existing is null) Files.Add(new(dto, _save));
            else existing.Refresh(dto);
        }
        Changed(nameof(Content));
        Changed(nameof(HasText));
        Changed(nameof(IsPending));
        Changed(nameof(IsFailed));
        Changed(nameof(ShowSendState));
        Changed(nameof(HasFiles));
        Changed(nameof(Time));
        Changed(nameof(IsEdited));
        Changed(nameof(CanEdit));
    }

    private static bool Same(AttachmentDto left, AttachmentDto right) =>
        left.Id != Guid.Empty && left.Id == right.Id
        || left.Id == Guid.Empty && right.FileName == left.FileName && right.Size == left.Size;
}
