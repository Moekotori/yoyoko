using System.Runtime.InteropServices;

namespace Chat.Media.NativeBridge;

// Control ABI only; future worker owns these calls. Never pass PCM/video through C#.
internal static partial class NativeMethods
{
    [LibraryImport("native_media_core", EntryPoint = "media_abi_version")]
    internal static partial uint AbiVersion();
}
