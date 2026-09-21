using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
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
    private bool _fixture;
    private bool _enter;
    private bool _moderate;
    private bool _mentioned;
    private string _replyLabel = "";
    public MessageRow(TimelineItem item, string author, Func<AttachmentDto, Task> save,
        Func<TimelineItem, Task> retry, Func<TimelineItem, Task> cancel, Action<Exception> onError, bool own)
    {
        Item = item;
        _author = author;
        _save = save;
        _own = own;
        Retry = new AsyncCommand(() => retry(Item), onError);
        Cancel = new AsyncCommand(() => cancel(Item), onError);
        Update();
    }

    private MessageRow()
    {
        IsUnreadDivider = true;
        Item = null!;
        Retry = new ActionCommand(_ => { });
        Cancel = new ActionCommand(_ => { });
    }

    private MessageRow(TimelineItem item, string author)
    {
        Item = item;
        _author = author;
        _fixture = true;
        Retry = new ActionCommand(_ => { });
        Cancel = new ActionCommand(_ => { });
        Update();
    }

    public static MessageRow UnreadDivider() => new();
    public static MessageRow Fixture(TimelineItem item, string author) => new(item, author);
    public ICommand Retry { get; }
    public ICommand Cancel { get; }
    public TimelineItem Item { get; private set; }
    public bool IsUnreadDivider { get; }
    public bool IsFixture => _fixture;
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
    public bool IsOwn { get => _own; private set { if (_own == value) return; _own = value; Changed(); Changed(nameof(CanEdit)); Changed(nameof(CanDelete)); } }
    public PlacementMode CardPlacement => PlacementMode.RightEdgeAlignedTop;
    public PlacementMode NamePlacement => PlacementMode.BottomEdgeAlignedLeft;
    public bool CanEdit => IsOwn && !IsFixture && !IsUnreadDivider && !IsFailed && Item.Status == SendStatus.Sent;
    public bool CanDelete => !IsFixture && !IsUnreadDivider && Item.Status == SendStatus.Sent && (IsOwn || _moderate);
    public bool CanReply => !IsFixture && !IsUnreadDivider && Item.Status == SendStatus.Sent;
    public bool HasReply => !IsUnreadDivider && Item.Message.ReplyTo is not null;
    public string ReplyLabel { get => _replyLabel; set { if (_replyLabel == value) return; _replyLabel = value; Changed(); } }
    public bool IsMentioned { get => _mentioned; private set { if (_mentioned == value) return; _mentioned = value; Changed(); } }
    public void SetMentioned(bool value) => IsMentioned = value;
    public void SetModerate(bool value)
    {
        if (_moderate == value) return;
        _moderate = value;
        Changed(nameof(CanDelete));
    }
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
    public bool CanCancel => IsPending;
    public void RequestEnter() => _enter = !IsUnreadDivider;
    public bool ConsumeEnter()
    {
        if (!_enter) return false;
        _enter = false;
        return true;
    }
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
        Changed(nameof(CanCancel));
        Changed(nameof(HasFiles));
        Changed(nameof(Time));
        Changed(nameof(IsEdited));
        Changed(nameof(CanEdit));
        Changed(nameof(CanDelete));
        Changed(nameof(CanReply));
        Changed(nameof(HasReply));
    }

    private static bool Same(AttachmentDto left, AttachmentDto right) =>
        left.Id != Guid.Empty && left.Id == right.Id
        || left.Id == Guid.Empty && right.FileName == left.FileName && right.Size == left.Size;
}
