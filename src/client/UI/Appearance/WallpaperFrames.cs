using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace Chat.UI.Appearance;

internal static class WallpaperFrames
{
    public static WriteableBitmap Create(PixelSize size)
    {
        size = WallpaperBudget.Clamp(size);
        return new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
    }

    public static WriteableBitmap FromSkia(SKBitmap source)
    {
        var bitmap = Create(new PixelSize(source.Width, source.Height));
        Copy(source.GetPixels(), source.Width, source.Height, source.RowBytes, bitmap);
        return bitmap;
    }

    public static unsafe void Copy(IntPtr source, int width, int height, int stride, WriteableBitmap destination)
    {
        using var frame = destination.Lock();
        var row = Math.Min(width * 4, Math.Min(stride, frame.RowBytes));
        if (stride == frame.RowBytes && row == stride)
            NativeMemory.Copy((void*)source, (void*)frame.Address, (nuint)(stride * height));
        else
        {
            for (var y = 0; y < height; y++)
                NativeMemory.Copy(
                    (void*)IntPtr.Add(source, y * stride),
                    (void*)IntPtr.Add(frame.Address, y * frame.RowBytes),
                    (nuint)row);
        }
    }

    public static void Copy(byte[] source, int width, int height, int stride, WriteableBitmap destination)
    {
        var handle = GCHandle.Alloc(source, GCHandleType.Pinned);
        try { Copy(handle.AddrOfPinnedObject(), width, height, stride, destination); }
        finally { handle.Free(); }
    }
}
