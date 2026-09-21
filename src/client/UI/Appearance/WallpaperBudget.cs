using Avalonia;

namespace Chat.UI.Appearance;

internal static class WallpaperBudget
{
    public const int MaxImageEdge = 1920;
    public const int MaxImagePixels = 1920 * 1080;
    public const int MaxVideoEdge = 1280;
    public const int MaxVideoPixels = 1280 * 720;
    public const int MaxImageBytes = 64 * 1024 * 1024;
    public const long MaxVideoBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxJpegFrameBytes = 2 * 1024 * 1024;
    public const int MaxFps = 20;
    public const int MinFrameMs = 1000 / MaxFps;

    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    public static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov", ".mkv", ".m4v"];

    public static PixelSize Clamp(PixelSize size, bool motion = false)
    {
        var maxEdge = motion ? MaxVideoEdge : MaxImageEdge;
        var maxPixels = motion ? MaxVideoPixels : MaxImagePixels;
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        if (width <= maxEdge && height <= maxEdge && (long)width * height <= maxPixels)
            return new(width, height);
        var scale = Math.Min(maxEdge / (double)Math.Max(width, height), Math.Sqrt(maxPixels / (double)((long)width * height)));
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
