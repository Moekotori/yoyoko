using Avalonia;

namespace Chat.UI.Appearance;

internal static class WallpaperBudget
{
    public const int MaxEdge = 3840;
    public const int MaxPixels = 3840 * 2160;
    public const int MaxImageBytes = 48 * 1024 * 1024;
    public const int MaxVideoBytes = 256 * 1024 * 1024;
    public const int MaxJpegFrameBytes = 8 * 1024 * 1024;
    public const int MaxFps = 24;
    public const int MinFrameMs = 1000 / MaxFps;

    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    public static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov", ".mkv", ".m4v"];

    public static PixelSize Clamp(PixelSize size)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        if (width <= MaxEdge && height <= MaxEdge && (long)width * height <= MaxPixels)
            return new(width, height);
        var scale = Math.Min(MaxEdge / (double)Math.Max(width, height), Math.Sqrt(MaxPixels / (double)((long)width * height)));
        return new(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension);
    public static bool IsVideo(string extension) => VideoExtensions.Contains(extension);

    public static string? NormalizeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".jpeg") extension = ".jpg";
        return IsImage(extension) || IsVideo(extension) ? extension : null;
    }
}
