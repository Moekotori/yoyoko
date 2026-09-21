using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Chat.Core.Voice;

namespace Chat.App.Transport;

internal sealed class MacNowPlayingControls : ISystemTransportControls
{
    private const int RtldNow = 2;
    private readonly object _gate = new();
    private nint _target;
    private nint _play;
    private nint _pause;
    private nint _toggle;
    private nint _stop;
    private nint _next;
    private nint _previous;
    private nint _center;
    private nint _titleKey;
    private nint _artistKey;
    private nint _albumKey;
    private nint _rateKey;
    private bool _ready;
    private bool _disposed;

    public bool Available => true;
    public event Action<TransportCommand>? CommandRequested;

    public void BindWindow(nint hwnd) { }

    public void Publish(TransportNowPlaying? session)
    {
        if (!OperatingSystem.IsMacOS()) return;
        lock (_gate)
        {
            if (_disposed) return;
            if (!_ready && session is null) return;
            if (!_ready && !Prepare()) return;
            if (session is null)
            {
                objc_msgSend(_center, sel_registerName("setPlaybackState:"), 3);
                objc_msgSend(_center, sel_registerName("setNowPlayingInfo:"), 0);
                SetEnabled(false);
                return;
            }
            SetEnabled(true);
            objc_msgSend(_center, sel_registerName("setPlaybackState:"), session.Muted ? 2 : 1);
            var info = objc_msgSend(objc_getClass("NSMutableDictionary"), sel_registerName("dictionary"));
            var title = string.IsNullOrEmpty(session.Channel) ? session.AppName : session.Channel;
            Set(info, _titleKey, title);
            Set(info, _artistKey, session.Community);
            Set(info, _albumKey, session.AppName);
            objc_msgSend(info, sel_registerName("setObject:forKey:"), Number(session.Muted ? 0 : 1), _rateKey);
            objc_msgSend(_center, sel_registerName("setNowPlayingInfo:"), info);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_ready)
            {
                objc_msgSend(_center, sel_registerName("setPlaybackState:"), 3);
                objc_msgSend(_center, sel_registerName("setNowPlayingInfo:"), 0);
                SetEnabled(false);
            }
        }
    }

    internal void Handle(TransportCommand command) => CommandRequested?.Invoke(command);

    private bool Prepare()
    {
        var media = dlopen("/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer", RtldNow);
        if (media == 0) return false;
        _titleKey = ReadSymbol(media, "MPMediaItemPropertyTitle");
        _artistKey = ReadSymbol(media, "MPMediaItemPropertyArtist");
        _albumKey = ReadSymbol(media, "MPMediaItemPropertyAlbumTitle");
        _rateKey = ReadSymbol(media, "MPNowPlayingInfoPropertyPlaybackRate");
        if (_titleKey == 0 || _artistKey == 0 || _albumKey == 0 || _rateKey == 0) return false;
        _center = objc_msgSend(objc_getClass("MPNowPlayingInfoCenter"), sel_registerName("defaultCenter"));
        var commands = objc_msgSend(objc_getClass("MPRemoteCommandCenter"), sel_registerName("sharedCommandCenter"));
        _play = objc_msgSend(commands, sel_registerName("playCommand"));
        _pause = objc_msgSend(commands, sel_registerName("pauseCommand"));
        _toggle = objc_msgSend(commands, sel_registerName("togglePlayPauseCommand"));
        _stop = objc_msgSend(commands, sel_registerName("stopCommand"));
        _next = objc_msgSend(commands, sel_registerName("nextTrackCommand"));
        _previous = objc_msgSend(commands, sel_registerName("previousTrackCommand"));
        _target = TargetNative.Create(this);
        var action = sel_registerName("handleCommand:");
        Add(_play, action);
        Add(_pause, action);
        Add(_toggle, action);
        Add(_stop, action);
        // MPRemoteCommandCenter owns macOS command delivery. The similarly named
        // UIKit beginReceivingRemoteControlEvents selector does not exist on NSApplication.
        _ready = true;
        return true;
    }

    private void Add(nint command, nint action)
        => objc_msgSend(command, sel_registerName("addTarget:action:"), _target, action);

    private void SetEnabled(bool enabled)
    {
        var flag = enabled ? (byte)1 : (byte)0;
        var sel = sel_registerName("setEnabled:");
        objc_msgSend_setEnabled(_play, sel, flag);
        objc_msgSend_setEnabled(_pause, sel, flag);
        objc_msgSend_setEnabled(_toggle, sel, flag);
        objc_msgSend_setEnabled(_stop, sel, flag);
        objc_msgSend_setEnabled(_next, sel, 0);
        objc_msgSend_setEnabled(_previous, sel, 0);
    }

    private static void Set(nint dictionary, nint key, string value)
        => objc_msgSend(dictionary, sel_registerName("setObject:forKey:"), NsString(value), key);

    private static nint Number(int value)
        => objc_msgSend(objc_getClass("NSNumber"), sel_registerName("numberWithInt:"), value);

    private static nint NsString(string value)
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes(value + '\0');
        unsafe
        {
            fixed (byte* pointer = utf8)
                return objc_msgSend(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), (nint)pointer);
        }
    }

    private static nint ReadSymbol(nint library, string name)
    {
        var symbol = dlsym(library, name);
        return symbol == 0 ? 0 : Marshal.ReadIntPtr(symbol);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector, nint arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector, nint arg1, nint arg2);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector, int arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_setEnabled(nint receiver, nint selector, byte enabled);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint objc_lookUpClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_registerClassPair(nint cls);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern byte class_addMethod(nint cls, nint name, nint imp, string types);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint object_setInstanceVariable(nint obj, string name, nint value);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint object_getInstanceVariable(nint obj, string name, out nint outValue);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern byte class_addIvar(nint cls, string name, nint size, byte alignment, string types);

    [DllImport("libdl.dylib")]
    private static extern nint dlopen(string path, int mode);

    [DllImport("libdl.dylib")]
    private static extern nint dlsym(nint handle, string name);

    private static class TargetNative
    {
        private const string ClassName = "ChatOsTransportTarget";

        public static unsafe nint Create(MacNowPlayingControls owner)
        {
            var cls = objc_lookUpClass(ClassName);
            if (cls == 0)
            {
                cls = objc_allocateClassPair(objc_getClass("NSObject"), ClassName, 0);
                class_addIvar(cls, "_owner", nint.Size, 3, "^v");
                class_addMethod(cls, sel_registerName("handleCommand:"),
                    (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint>)&HandleCommand, "q@:@");
                objc_registerClassPair(cls);
            }
            var instance = objc_msgSend(cls, sel_registerName("new"));
            var handle = GCHandle.Alloc(owner);
            object_setInstanceVariable(instance, "_owner", GCHandle.ToIntPtr(handle));
            return instance;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static nint HandleCommand(nint self, nint cmd, nint evt)
        {
            _ = cmd;
            object_getInstanceVariable(self, "_owner", out var pointer);
            if (pointer == 0) return 0;
            var handle = GCHandle.FromIntPtr(pointer);
            if (handle.Target is not MacNowPlayingControls owner) return 0;
            var command = CommandOf(evt);
            owner.Handle(command);
            return 0;
        }

        private static TransportCommand CommandOf(nint evt)
        {
            var command = objc_msgSend(evt, sel_registerName("command"));
            if (command == 0) return TransportCommand.ToggleMute;
            var pause = objc_msgSend(objc_msgSend(objc_getClass("MPRemoteCommandCenter"), sel_registerName("sharedCommandCenter")),
                sel_registerName("pauseCommand"));
            var stop = objc_msgSend(objc_msgSend(objc_getClass("MPRemoteCommandCenter"), sel_registerName("sharedCommandCenter")),
                sel_registerName("stopCommand"));
            var play = objc_msgSend(objc_msgSend(objc_getClass("MPRemoteCommandCenter"), sel_registerName("sharedCommandCenter")),
                sel_registerName("playCommand"));
            if (command == play) return TransportCommand.Unmute;
            if (command == pause || command == stop) return TransportCommand.Mute;
            return TransportCommand.ToggleMute;
        }
    }
}
