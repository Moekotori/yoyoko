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

public sealed class AttachmentRow : ObservableObject
{
    private Bitmap? _preview;
    private bool _saving;
    public AttachmentRow(AttachmentDto dto, Func<AttachmentDto, Task> save)
    {
        Dto = dto;
        Save = new AsyncCommand(async () =>
        {
            IsSaving = true;
            try { await save(Dto); }
            finally { IsSaving = false; }
        }, _ => { IsSaving = false; });
    }
    public AttachmentDto Dto { get; private set; }
    public string FileName => Dto.FileName;
    public string SizeLabel => FileKinds.SizeLabel(Dto.Size);
    public string Label => FileName + " · " + SizeLabel;
    public bool IsImage => FileKinds.IsImage(Dto.MimeType);
    public bool ShowChip => !IsImage || !HasPreview;
    public Uri? PreviewUrl => Dto.ThumbnailUrl;
    public ICommand Save { get; }
    public bool IsSaving
    {
        get => _saving;
        private set { _saving = value; Changed(); }
    }
    public Bitmap? Preview
    {
        get => _preview;
        set { _preview = value; Changed(); Changed(nameof(HasPreview)); Changed(nameof(ShowChip)); }
    }
    public bool HasPreview => _preview is not null;
    public void Refresh(AttachmentDto dto)
    {
        Dto = dto;
        Changed(nameof(FileName));
        Changed(nameof(SizeLabel));
        Changed(nameof(Label));
        Changed(nameof(IsImage));
        Changed(nameof(PreviewUrl));
        Changed(nameof(ShowChip));
    }
}

public sealed class MessageRow : ObservableObject
{
    private readonly Func<AttachmentDto, Task> _save;
    private string _author;
    private AvatarPlayback? _playback;
    private bool _continuation;
    public MessageRow(TimelineItem item, string author, Func<AttachmentDto, Task> save,
        Func<TimelineItem, Task> retry, Action<Exception> onError)
    {
        Item = item;
        _author = author;
        _save = save;
        Retry = new AsyncCommand(() => retry(Item), onError);
        Update();
    }
    public ICommand Retry { get; }
    public TimelineItem Item { get; private set; }
    public Guid Id => Item.Message.Id;
    public string Author { get => _author; private set { _author = value; Changed(); Changed(nameof(Initial)); } }
    public string Initial => Avatar.FromName(_author);
    public void SetAuthor(string author) => Author = author;
    public AvatarPlayback? Playback
    {
        get => _playback;
        set { _playback = value; Changed(); }
    }
    public string Content => Item.Message.Content ?? "";
    public bool HasText => !string.IsNullOrEmpty(Content);
    public string Time => Item.Message.CreatedAt.ToLocalTime().ToString("HH:mm");
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
    public bool ShowHeader => !IsContinuation;
    public bool IsPending => Item.Status == SendStatus.Sending;
    public bool IsFailed => Item.Status == SendStatus.Failed;
    public bool ShowSendState => IsPending || IsFailed;
    public void SetContinuation(bool value) => IsContinuation = value;
    public ObservableCollection<AttachmentRow> Files { get; } = [];
    public bool HasFiles => Files.Count > 0;
    public void Update(TimelineItem? item = null)
    {
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
    }

    private static bool Same(AttachmentDto left, AttachmentDto right) =>
        left.Id != Guid.Empty && left.Id == right.Id
        || left.Id == Guid.Empty && right.FileName == left.FileName && right.Size == left.Size;
}
