using System.Windows.Input;
using Avalonia.Media.Imaging;
using Chat.Core.Messaging;
using Chat.Protocol;
using Chat.UI.Components;

namespace Chat.UI.Chat;

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
