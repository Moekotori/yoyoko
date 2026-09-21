using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Chat.Core.Voice;

namespace Chat.App.Transport;

internal sealed class WindowsSmtcControls : ISystemTransportControls
{
    private static readonly Guid InteropIid = new("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
    private static readonly Guid ControlsIid = new("99fa3ff4-1742-42a6-902e-087d41f965ec");
    private static readonly Guid Music2Iid = new("00368462-97d3-44b9-b00f-008afcefaf18");
    private static readonly Guid Controls2Iid = new("ea98d2f6-7f3c-4af2-a586-72889808efb1");
    private static readonly Guid HandlerIid = new("0557e996-7b23-5bae-aa81-ea0d671143a4");
    private static readonly Guid ButtonArgsIid = new("b7f47116-a56f-4dc8-9e11-92031f4a87c2");
    private static readonly Guid IUnknownIid = new("00000000-0000-0000-c000-000000000046");
    private static readonly Guid IAgileObjectIid = new("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");

    private readonly object _gate = new();
    private nint _hwnd;
    private nint _controls;
    private nint _handler;
    private long _buttonToken;
    private bool _listening;
    private TransportNowPlaying? _pending;
    private bool _disposed;

    public bool Available => true;
    public event Action<TransportCommand>? CommandRequested;
    public event Action? RaiseRequested { add { } remove { } }

    public void BindWindow(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == 0) return;
        lock (_gate)
        {
            if (_disposed || _hwnd == hwnd) return;
            _hwnd = hwnd;
        }
        Ensure();
        TransportNowPlaying? pending;
        lock (_gate) pending = _pending;
        if (pending is not null) Apply(pending);
        else Apply(null);
    }

    public void Publish(TransportNowPlaying? session)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = session;
        }
        if (Volatile.Read(ref _controls) == 0 && session is not null) Ensure();
        Apply(session);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Apply(null);
        TearDown();
    }

    private void Ensure()
    {
        if (!OperatingSystem.IsWindows()) return;
        nint hwnd;
        lock (_gate)
        {
            if (_disposed || _controls != 0) return;
            hwnd = _hwnd;
        }
        if (hwnd == 0) return;
        try
        {
            var init = RoInitialize(0);
            if (init < 0 && init != unchecked((int)0x80010106)) return;
            var className = HString("Windows.Media.SystemMediaTransportControls");
            nint factory;
            try
            {
                if (RoGetActivationFactory(className, InteropIid, out factory) != 0 || factory == 0)
                    return;
            }
            finally { WindowsDeleteString(className); }
            try
            {
                if (Call3(factory, 6, hwnd, ControlsIid, out var controls) != 0 || controls == 0) return;
                var handler = HandlerNative.Alloc(this);
                if (CallAdd(controls, 32, handler, out var token) != 0)
                {
                    HandlerNative.Release(handler);
                    Release(controls);
                    return;
                }
                lock (_gate)
                {
                    if (_disposed)
                    {
                        CallRemove(controls, 33, token);
                        HandlerNative.Release(handler);
                        Release(controls);
                        return;
                    }
                    _controls = controls;
                    _handler = handler;
                    _buttonToken = token;
                    _listening = true;
                }
            }
            finally { Release(factory); }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private void TearDown()
    {
        nint controls;
        nint handler;
        long token;
        bool listening;
        lock (_gate)
        {
            controls = _controls;
            handler = _handler;
            token = _buttonToken;
            listening = _listening;
            _controls = 0;
            _handler = 0;
            _buttonToken = 0;
            _listening = false;
        }
        if (listening && controls != 0) CallRemove(controls, 33, token);
        if (handler != 0) HandlerNative.Release(handler);
        if (controls != 0) Release(controls);
    }

    private void Apply(TransportNowPlaying? session)
    {
        var controls = Volatile.Read(ref _controls);
        if (controls == 0) return;
        try
        {
            if (session is null)
            {
                if (CallGet(controls, 8, out var clear) == 0 && clear != 0)
                {
                    try { Call(clear, 16); Call(clear, 17); }
                    finally { Release(clear); }
                }
                CallPutInt(controls, 11, 0);
                CallPutInt(controls, 7, 0);
                return;
            }
            CallPutInt(controls, 11, 1);
            CallPutInt(controls, 13, session.Muted ? 1 : 0);
            CallPutInt(controls, 17, session.Muted ? 0 : 1);
            CallPutInt(controls, 15, 0);
            CallPutInt(controls, 19, 0);
            CallPutInt(controls, 21, 0);
            CallPutInt(controls, 23, 0);
            CallPutInt(controls, 25, 0);
            CallPutInt(controls, 27, 0);
            CallPutInt(controls, 29, 0);
            CallPutInt(controls, 31, 0);
            CallPutInt(controls, 7, session.Muted ? 4 : 3);
            if (Query(controls, Controls2Iid, out var controls2) == 0 && controls2 != 0)
            {
                try
                {
                    CallPutInt(controls2, 7, 0);
                    CallPutInt(controls2, 9, 0);
                }
                finally { Release(controls2); }
            }
            if (CallGet(controls, 8, out var updater) != 0 || updater == 0) return;
            try
            {
                CallPutInt(updater, 7, 1);
                PutHString(updater, 9, session.AppName);
                if (CallGet(updater, 12, out var music) == 0 && music != 0)
                {
                    try
                    {
                        var title = string.IsNullOrEmpty(session.Channel) ? session.AppName : session.Channel;
                        PutHString(music, 7, title);
                        PutHString(music, 11, session.Community);
                        if (Query(music, Music2Iid, out var music2) == 0 && music2 != 0)
                        {
                            try { PutHString(music2, 7, session.AppName); }
                            finally { Release(music2); }
                        }
                    }
                    finally { Release(music); }
                }
                Call(updater, 17);
            }
            finally { Release(updater); }
        }
        catch (Exception) { }
    }

    internal void OnButton(int button)
    {
        var command = button switch
        {
            0 => TransportCommand.Unmute,
            1 or 2 => TransportCommand.Mute,
            _ => (TransportCommand?)null
        };
        if (command is { } value) CommandRequested?.Invoke(value);
    }

    private static void PutHString(nint obj, int slot, string value)
    {
        var h = HString(value);
        try { CallPutPtr(obj, slot, h); }
        finally { WindowsDeleteString(h); }
    }

    private static nint HString(string value)
    {
        var hr = WindowsCreateString(value, value.Length, out var handle);
        return hr == 0 ? handle : 0;
    }

    private static nint Vtable(nint obj) => Marshal.ReadIntPtr(obj);
    private static nint Slot(nint obj, int index) => Marshal.ReadIntPtr(Vtable(obj), index * nint.Size);

    private static unsafe int Call(nint obj, int slot)
        => ((delegate* unmanaged[Stdcall]<nint, int>)Slot(obj, slot))(obj);

    private static unsafe int CallGet(nint obj, int slot, out nint value)
    {
        nint local;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(obj, slot))(obj, &local);
        value = local;
        return hr;
    }

    private static unsafe int CallGetInt(nint obj, int slot, out int value)
    {
        int local;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(obj, slot))(obj, &local);
        value = local;
        return hr;
    }

    private static unsafe int CallPutInt(nint obj, int slot, int value)
        => ((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(obj, slot))(obj, value);

    private static unsafe int CallPutPtr(nint obj, int slot, nint value)
        => ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(obj, slot))(obj, value);

    private static unsafe int Call3(nint obj, int slot, nint hwnd, Guid iid, out nint value)
    {
        nint local;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Slot(obj, slot))(obj, hwnd, &iid, &local);
        value = local;
        return hr;
    }

    private static unsafe int CallAdd(nint obj, int slot, nint handler, out long token)
    {
        long local;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(obj, slot))(obj, handler, &local);
        token = local;
        return hr;
    }

    private static unsafe int CallRemove(nint obj, int slot, long token)
        => ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(obj, slot))(obj, token);

    private static unsafe int Query(nint obj, Guid iid, out nint value)
    {
        nint local;
        var hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(obj, 0))(obj, &iid, &local);
        value = local;
        return hr;
    }

    private static unsafe void Release(nint obj)
    {
        if (obj == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint hstring);

    private unsafe struct HandlerNative
    {
        public nint Vtable;
        public int RefCount;
        public GCHandle Owner;

        private static readonly nint* Vtbl = CreateVtbl();

        public static unsafe nint Alloc(WindowsSmtcControls owner)
        {
            var memory = (HandlerNative*)NativeMemory.Alloc((nuint)sizeof(HandlerNative));
            memory->Vtable = (nint)Vtbl;
            memory->RefCount = 1;
            memory->Owner = GCHandle.Alloc(owner);
            return (nint)memory;
        }

        public static unsafe void Release(nint pointer)
        {
            var self = (HandlerNative*)pointer;
            var left = Interlocked.Decrement(ref self->RefCount);
            if (left != 0) return;
            if (self->Owner.IsAllocated) self->Owner.Free();
            NativeMemory.Free(self);
        }

        private static unsafe nint* CreateVtbl()
        {
            var table = (nint*)NativeMemory.Alloc((nuint)(4 * sizeof(nint)));
            table[0] = (nint)(delegate* unmanaged[Stdcall]<HandlerNative*, Guid*, nint*, int>)&QueryInterface;
            table[1] = (nint)(delegate* unmanaged[Stdcall]<HandlerNative*, uint>)&AddRef;
            table[2] = (nint)(delegate* unmanaged[Stdcall]<HandlerNative*, uint>)&Release;
            table[3] = (nint)(delegate* unmanaged[Stdcall]<HandlerNative*, nint, nint, int>)&Invoke;
            return table;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int QueryInterface(HandlerNative* self, Guid* riid, nint* ppv)
        {
            var id = *riid;
            if (id == IUnknownIid || id == IAgileObjectIid || id == HandlerIid)
            {
                *ppv = (nint)self;
                Interlocked.Increment(ref self->RefCount);
                return 0;
            }
            *ppv = 0;
            return unchecked((int)0x80004002);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint AddRef(HandlerNative* self)
            => (uint)Interlocked.Increment(ref self->RefCount);

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static uint Release(HandlerNative* self)
        {
            var left = Interlocked.Decrement(ref self->RefCount);
            if (left == 0)
            {
                if (self->Owner.IsAllocated) self->Owner.Free();
                NativeMemory.Free(self);
            }
            return (uint)left;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int Invoke(HandlerNative* self, nint sender, nint args)
        {
            _ = sender;
            if (!self->Owner.IsAllocated || self->Owner.Target is not WindowsSmtcControls owner)
                return 0;
            if (Query(args, ButtonArgsIid, out var typed) != 0 || typed == 0)
                return 0;
            try
            {
                if (CallGetInt(typed, 6, out var button) == 0)
                    owner.OnButton(button);
            }
            finally { Release(typed); }
            return 0;
        }
    }
}
