using Avalonia.Media.Imaging;
using Chat.Core.Messaging;
using Chat.Protocol;
using Chat.UI.Components;

namespace Chat.UI.Chat;

public sealed class MessageRow : ObservableObject
{
    private Bitmap? _image;
    public MessageRow(TimelineItem item, string author)
    {
        Item = item;
        Author = author;
        Update();
    }
    public TimelineItem Item { get; }
    public Guid Id => Item.Message.Id;
    public string Author { get; }
    public string Content => Item.Message.Content ?? "";
    public bool HasText => !string.IsNullOrEmpty(Content);
    public string Time => Item.Message.CreatedAt.ToLocalTime().ToString("HH:mm");
    public bool IsPending => Item.Status == SendStatus.Sending;
    public bool IsFailed => Item.Status == SendStatus.Failed;
    public bool HasImage => Item.Message.Attachments is { Length: > 0 };
    public Uri? ImageUrl => Item.Message.Attachments is { Length: > 0 } attachments
        ? attachments[0].ThumbnailUrl ?? attachments[0].DownloadUrl
        : null;
    public Bitmap? Image
    {
        get => _image;
        set { _image = value; Changed(); Changed(nameof(HasBitmap)); Changed(nameof(ShowImagePlaceholder)); }
    }
    public bool HasBitmap => _image is not null;
    public bool ShowImagePlaceholder => HasImage && _image is null;
    public void Update()
    {
        Changed(nameof(Content));
        Changed(nameof(HasText));
        Changed(nameof(IsPending));
        Changed(nameof(IsFailed));
        Changed(nameof(HasImage));
        Changed(nameof(Time));
    }
}
