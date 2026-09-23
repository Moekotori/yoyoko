using Avalonia.Platform.Storage;
using Chat.Core.Messaging;
using Chat.Core.Sessions;

namespace Chat.UI.Chat;

internal static class IncomingFiles
{
    // Include one extra item so QueueFilesAsync can surface the file-count error.
    internal const int MaxUiAttachments = 16;

    public static async Task<IReadOnlyList<PickedFile>> OpenAsync(IEnumerable<IStorageItem> items, int attachmentLimit)
    {
        var candidateLimit = Math.Clamp(attachmentLimit, 1, MaxUiAttachments) + 1;
        var opened = new List<PickedFile>(candidateLimit);
        try
        {
            foreach (var file in items.OfType<IStorageFile>().Take(candidateLimit))
            {
                Stream? stream = null;
                try
                {
                    var path = file.TryGetLocalPath();
                    long size;
                    if (path is not null && File.Exists(path))
                    {
                        var info = new FileInfo(path);
                        size = info.Length;
                        stream = File.OpenRead(path);
                    }
                    else
                    {
                        var properties = await file.GetBasicPropertiesAsync();
                        size = (long)(properties.Size ?? 0);
                        stream = await file.OpenReadAsync();
                    }
                    opened.Add(new PickedFile(file.Name, FileKinds.MimeFromFileName(file.Name), stream, size));
                    stream = null;
                }
                finally { stream?.Dispose(); }
            }
            return opened;
        }
        catch
        {
            foreach (var file in opened) await file.DisposeAsync();
            throw;
        }
    }
}
