using Chat.Protocol;

namespace Chat.Core.Messaging;

public static class FileKinds
{
    public static bool IsImage(string mime) =>
        mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        && !mime.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase);

    public static string MimeFromFileName(string fileName) =>
        Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "pdf" => "application/pdf",
            "txt" or "log" => "text/plain",
            "md" => "text/markdown",
            "csv" => "text/csv",
            "json" => "application/json",
            "xml" => "application/xml",
            "zip" => "application/zip",
            "gz" => "application/gzip",
            "tar" => "application/x-tar",
            "7z" => "application/x-7z-compressed",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "ogg" => "audio/ogg",
            "m4a" => "audio/mp4",
            "mp4" => "video/mp4",
            "webm" => "video/webm",
            "mov" => "video/quicktime",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ppt" => "application/vnd.ms-powerpoint",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };

    public static string SizeLabel(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB"
    };

    public static long EffectiveLimit(long advertised) =>
        advertised > 0 ? advertised : ProtocolVersion.MaxAttachmentBytes;

    public static int EffectiveCount(int advertised) =>
        advertised > 0 ? advertised : ProtocolVersion.MaxAttachmentsPerMessage;
}
