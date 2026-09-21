using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Chat.Core.Voice;

namespace Chat.App.Transport;

internal sealed class LinuxMprisControls : ISystemTransportControls
{
    private const string PathName = "/org/mpris/MediaPlayer2";
    private const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    private const string AppInterface = "org.mpris.MediaPlayer2";
    private readonly object _gate = new();
    private readonly string _busName = "org.mpris.MediaPlayer2.chat.instance" + Environment.ProcessId;
    private Socket? _socket;
    private Thread? _reader;
    private CancellationTokenSource? _lifetime;
    private uint _serial = 1;
    private TransportNowPlaying? _current;
    private bool _disposed;

    public bool Available => true;
    public event Action<TransportCommand>? CommandRequested;

    public void BindWindow(nint hwnd) { }

    public void Publish(TransportNowPlaying? session)
    {
        if (!OperatingSystem.IsLinux()) return;
        lock (_gate)
        {
            if (_disposed) return;
            _current = session;
            if (session is null)
            {
                Close();
                return;
            }
            if (_socket is null && !Connect()) return;
            EmitChanged(session);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }

    private bool Connect()
    {
        var path = BusPath();
        if (path is null) return false;
        try
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
            Authenticate(socket);
            _socket = socket;
            _lifetime = new CancellationTokenSource();
            Hello();
            RequestName();
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "mpris" };
            _reader.Start();
            return true;
        }
        catch (Exception)
        {
            Close();
            return false;
        }
    }

    private void Close()
    {
        try { _lifetime?.Cancel(); } catch (ObjectDisposedException) { }
        try { _socket?.Dispose(); } catch (Exception) { }
        _socket = null;
        _lifetime?.Dispose();
        _lifetime = null;
        _reader = null;
    }

    private void ReadLoop()
    {
        var token = _lifetime?.Token ?? CancellationToken.None;
        try
        {
            while (!token.IsCancellationRequested && _socket is { Connected: true } socket)
            {
                if (!TryReadMessage(socket, out var message)) break;
                if (message.Type != 1) continue;
                HandleCall(message);
            }
        }
        catch (Exception) { }
    }

    private void HandleCall(DbusMessage message)
    {
        Socket socket;
        TransportNowPlaying? session;
        lock (_gate)
        {
            if (_socket is null) return;
            socket = _socket;
            session = _current;
        }
        if (message.Member is "PlayPause" or "Play" or "Pause" or "Stop")
        {
            Reply(socket, message, "");
            var command = message.Member switch
            {
                "Play" => TransportCommand.Unmute,
                "Pause" or "Stop" => TransportCommand.Mute,
                _ => TransportCommand.ToggleMute
            };
            CommandRequested?.Invoke(command);
            return;
        }
        if (message.InterfaceName == "org.freedesktop.DBus.Properties" && message.Member == "Get" && session is not null)
        {
            var iface = message.BodyStrings.Count > 0 ? message.BodyStrings[0] : "";
            var name = message.BodyStrings.Count > 1 ? message.BodyStrings[1] : "";
            ReplyVariant(socket, message, Property(session, iface, name));
            return;
        }
        if (message.InterfaceName == "org.freedesktop.DBus.Properties" && message.Member == "GetAll" && session is not null)
        {
            var iface = message.BodyStrings.Count > 0 ? message.BodyStrings[0] : PlayerInterface;
            Reply(socket, message, "a{sv}", writer => WriteAll(writer, session, iface));
            return;
        }
        if (message.Member == "Introspect")
        {
            Reply(socket, message, "s", writer => writer.String(IntrospectXml));
            return;
        }
        Reply(socket, message, "");
    }

    private void EmitChanged(TransportNowPlaying session)
    {
        if (_socket is null) return;
        var payload = new DbusWriter();
        payload.String(PlayerInterface);
        payload.OpenArray("{sv}");
        WriteEntry(payload, "PlaybackStatus", "s", writer => writer.String(session.Muted ? "Paused" : "Playing"));
        WriteEntry(payload, "Metadata", "a{sv}", writer => WriteMetadata(writer, session));
        payload.CloseArray();
        payload.OpenArray("s");
        payload.CloseArray();
        Send(_socket, 4, "sa{sv}as", payload.AsSpan(),
            path: PathName, interfaceName: "org.freedesktop.DBus.Properties", member: "PropertiesChanged");
    }

    private void Hello()
    {
        if (_socket is null) return;
        Send(_socket, 1, "", ReadOnlySpan<byte>.Empty, destination: "org.freedesktop.DBus",
            path: "/org/freedesktop/DBus", interfaceName: "org.freedesktop.DBus", member: "Hello");
        _ = TryReadMessage(_socket, out _);
    }

    private void RequestName()
    {
        if (_socket is null) return;
        var body = new DbusWriter();
        body.String(_busName);
        body.UInt32(4);
        Send(_socket, 1, "su", body.AsSpan(), destination: "org.freedesktop.DBus",
            path: "/org/freedesktop/DBus", interfaceName: "org.freedesktop.DBus", member: "RequestName");
        _ = TryReadMessage(_socket, out _);
    }

    private void Reply(Socket socket, DbusMessage call, string signature, Action<DbusWriter>? body = null)
    {
        var writer = new DbusWriter();
        body?.Invoke(writer);
        Send(socket, 2, signature, writer.AsSpan(), destination: call.Sender, replySerial: call.Serial);
    }

    private void ReplyVariant(Socket socket, DbusMessage call, (string Signature, Action<DbusWriter> Write)? property)
    {
        var writer = new DbusWriter();
        if (property is { } value)
        {
            writer.Signature(value.Signature);
            value.Write(writer);
        }
        else
        {
            writer.Signature("s");
            writer.String("");
        }
        Send(socket, 2, "v", writer.AsSpan(), destination: call.Sender, replySerial: call.Serial);
    }

    private (string Signature, Action<DbusWriter> Write)? Property(TransportNowPlaying session, string iface, string name)
        => (iface, name) switch
        {
            (AppInterface, "Identity") => ("s", writer => writer.String(session.AppName)),
            (AppInterface, "CanQuit") or (AppInterface, "CanRaise") or (AppInterface, "HasTrackList")
                => ("b", writer => writer.Boolean(false)),
            (PlayerInterface, "PlaybackStatus") => ("s", writer => writer.String(session.Muted ? "Paused" : "Playing")),
            (PlayerInterface, "Metadata") => ("a{sv}", writer => WriteMetadata(writer, session)),
            (PlayerInterface, "CanPlay") or (PlayerInterface, "CanPause") or (PlayerInterface, "CanControl")
                => ("b", writer => writer.Boolean(true)),
            (PlayerInterface, "CanGoNext") or (PlayerInterface, "CanGoPrevious") or (PlayerInterface, "CanSeek")
                => ("b", writer => writer.Boolean(false)),
            (PlayerInterface, "Rate") or (PlayerInterface, "MinimumRate") or (PlayerInterface, "MaximumRate") or (PlayerInterface, "Volume")
                => ("d", writer => writer.Double(1)),
            (PlayerInterface, "Position") => ("x", writer => writer.Int64(0)),
            _ => null
        };

    private static void WriteAll(DbusWriter writer, TransportNowPlaying session, string iface)
    {
        writer.OpenArray("{sv}");
        if (iface == AppInterface)
        {
            WriteEntry(writer, "Identity", "s", w => w.String(session.AppName));
            WriteEntry(writer, "CanQuit", "b", w => w.Boolean(false));
            WriteEntry(writer, "CanRaise", "b", w => w.Boolean(false));
            WriteEntry(writer, "HasTrackList", "b", w => w.Boolean(false));
        }
        else
        {
            WriteEntry(writer, "PlaybackStatus", "s", w => w.String(session.Muted ? "Paused" : "Playing"));
            WriteEntry(writer, "Metadata", "a{sv}", w => WriteMetadata(w, session));
            WriteEntry(writer, "CanPlay", "b", w => w.Boolean(true));
            WriteEntry(writer, "CanPause", "b", w => w.Boolean(true));
            WriteEntry(writer, "CanControl", "b", w => w.Boolean(true));
            WriteEntry(writer, "CanGoNext", "b", w => w.Boolean(false));
            WriteEntry(writer, "CanGoPrevious", "b", w => w.Boolean(false));
            WriteEntry(writer, "CanSeek", "b", w => w.Boolean(false));
        }
        writer.CloseArray();
    }

    private static void WriteMetadata(DbusWriter writer, TransportNowPlaying session)
    {
        writer.OpenArray("{sv}");
        WriteEntry(writer, "mpris:trackid", "o", w => w.String("/org/mpris/MediaPlayer2/track/voice"));
        WriteEntry(writer, "xesam:title", "s", w => w.String(string.IsNullOrEmpty(session.Channel) ? session.AppName : session.Channel));
        WriteEntry(writer, "xesam:album", "s", w => w.String(session.AppName));
        WriteEntry(writer, "xesam:artist", "as", w =>
        {
            w.OpenArray("s");
            w.String(session.Community);
            w.CloseArray();
        });
        writer.CloseArray();
    }

    private static void WriteEntry(DbusWriter writer, string name, string signature, Action<DbusWriter> value)
    {
        writer.Align(8);
        writer.String(name);
        writer.Signature(signature);
        value(writer);
    }

    private void Send(Socket socket, byte type, string signature, ReadOnlySpan<byte> body,
        string? destination = null, string? path = null, string? interfaceName = null, string? member = null,
        uint replySerial = 0)
    {
        var serial = _serial++;
        var fields = new DbusWriter();
        if (path is not null) WriteField(fields, 1, "o", writer => writer.String(path));
        if (interfaceName is not null) WriteField(fields, 2, "s", writer => writer.String(interfaceName));
        if (member is not null) WriteField(fields, 3, "s", writer => writer.String(member));
        if (replySerial != 0) WriteField(fields, 5, "u", writer => writer.UInt32(replySerial));
        if (destination is not null) WriteField(fields, 6, "s", writer => writer.String(destination));
        if (signature.Length > 0) WriteField(fields, 8, "g", writer => writer.Signature(signature));
        var fieldBytes = fields.AsSpan();
        var header = new byte[12];
        header[0] = (byte)'l';
        header[1] = type;
        header[3] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)body.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), serial);
        var arrayHeader = new DbusWriter();
        arrayHeader.UInt32((uint)fieldBytes.Length);
        var prefix = arrayHeader.AsSpan();
        var fieldPad = Pad(12 + prefix.Length + fieldBytes.Length, 8);
        var message = new byte[12 + prefix.Length + fieldBytes.Length + fieldPad + body.Length];
        header.CopyTo(message, 0);
        prefix.CopyTo(message.AsSpan(12));
        fieldBytes.CopyTo(message.AsSpan(12 + prefix.Length));
        body.CopyTo(message.AsSpan(12 + prefix.Length + fieldBytes.Length + fieldPad));
        socket.Send(message);
    }

    private static void WriteField(DbusWriter writer, byte code, string signature, Action<DbusWriter> value)
    {
        writer.Align(8);
        writer.Byte(code);
        writer.Signature(signature);
        value(writer);
    }

    private static bool TryReadMessage(Socket socket, out DbusMessage message)
    {
        message = default;
        var header = new byte[16];
        if (!ReadExact(socket, header.AsSpan(0, 16))) return false;
        if (header[0] != (byte)'l' || header[3] != 1) return false;
        var bodyLen = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var fieldsLen = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
        var fieldsPad = Pad((int)fieldsLen, 8);
        var rest = new byte[fieldsLen + fieldsPad + bodyLen];
        if (!ReadExact(socket, rest)) return false;
        var fields = ParseFields(rest.AsSpan(0, (int)fieldsLen));
        var body = rest.AsSpan((int)fieldsLen + fieldsPad, (int)bodyLen);
        message = new DbusMessage(header[1], serial, fields.Path, fields.InterfaceName, fields.Member, fields.Sender,
            ReadStrings(body, fields.Signature));
        return true;
    }

    private static (string? Path, string? InterfaceName, string? Member, string? Sender, string Signature) ParseFields(
        ReadOnlySpan<byte> data)
    {
        string? path = null, iface = null, member = null, sender = null, signature = "";
        var offset = 0;
        while (offset < data.Length)
        {
            offset = Align(offset, 8);
            if (offset >= data.Length) break;
            var code = data[offset];
            offset++;
            var sig = ReadSignature(data, ref offset);
            switch (code)
            {
                case 1: path = ReadString(data, ref offset); break;
                case 2: iface = ReadString(data, ref offset); break;
                case 3: member = ReadString(data, ref offset); break;
                case 6: sender = ReadString(data, ref offset); break;
                case 8: signature = sig == "g" ? ReadSignature(data, ref offset) : ""; break;
                default: Skip(data, ref offset, sig); break;
            }
        }
        return (path, iface, member, sender, signature ?? "");
    }

    private static List<string> ReadStrings(ReadOnlySpan<byte> body, string signature)
    {
        var values = new List<string>();
        var offset = 0;
        foreach (var code in signature)
        {
            if (code is 's' or 'o' or 'g')
                values.Add(code == 'g' ? ReadSignature(body, ref offset) : ReadString(body, ref offset));
            else
                break;
        }
        return values;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        offset = Align(offset, 4);
        if (offset + 4 > data.Length) return "";
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        if (offset + length + 1 > data.Length) return "";
        var value = Encoding.UTF8.GetString(data.Slice(offset, length));
        offset += length + 1;
        return value;
    }

    private static string ReadSignature(ReadOnlySpan<byte> data, ref int offset)
    {
        if (offset >= data.Length) return "";
        var length = data[offset];
        offset++;
        if (offset + length + 1 > data.Length) return "";
        var value = Encoding.UTF8.GetString(data.Slice(offset, length));
        offset += length + 1;
        return value;
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int offset, string signature)
    {
        foreach (var code in signature)
        {
            switch (code)
            {
                case 's' or 'o': ReadString(data, ref offset); break;
                case 'g': ReadSignature(data, ref offset); break;
                case 'u' or 'i' or 'b':
                    offset = Align(offset, 4) + 4;
                    break;
                case 't' or 'x' or 'd':
                    offset = Align(offset, 8) + 8;
                    break;
                default:
                    return;
            }
        }
    }

    private static bool ReadExact(Socket socket, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = socket.Receive(buffer[offset..]);
            if (read <= 0) return false;
            offset += read;
        }
        return true;
    }

    private static void Authenticate(Socket socket)
    {
        var uid = Encoding.ASCII.GetBytes(geteuid().ToString());
        var hex = Convert.ToHexString(uid).ToLowerInvariant();
        socket.Send("\0"u8.ToArray());
        socket.Send(Encoding.ASCII.GetBytes("AUTH EXTERNAL " + hex + "\r\n"));
        var buffer = new byte[256];
        var read = socket.Receive(buffer);
        var reply = Encoding.ASCII.GetString(buffer, 0, Math.Max(read, 0));
        if (!reply.StartsWith("OK", StringComparison.Ordinal)) throw new InvalidOperationException("D-Bus auth failed.");
        socket.Send("BEGIN\r\n"u8.ToArray());
    }

    private static string? BusPath()
    {
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address))
        {
            var fallback = "/run/user/" + geteuid() + "/bus";
            return File.Exists(fallback) ? fallback : null;
        }
        foreach (var part in address.Split(';'))
        {
            if (!part.StartsWith("unix:", StringComparison.Ordinal)) continue;
            foreach (var pair in part[5..].Split(','))
            {
                if (pair.StartsWith("path=", StringComparison.Ordinal)) return pair[5..];
                if (pair.StartsWith("abstract=", StringComparison.Ordinal)) return "\0" + pair[9..];
            }
        }
        return null;
    }

    private static int Pad(int length, int align) => (align - (length % align)) % align;
    private static int Align(int offset, int align) => offset + Pad(offset, align);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern uint geteuid();

    private const string IntrospectXml =
        """
        <node><interface name="org.freedesktop.DBus.Introspectable"><method name="Introspect"><arg type="s" direction="out"/></method></interface></node>
        """;

    private readonly struct DbusMessage(byte type, uint serial, string? path, string? interfaceName, string? member,
        string? sender, List<string> bodyStrings)
    {
        public byte Type { get; } = type;
        public uint Serial { get; } = serial;
        public string? Path { get; } = path;
        public string? InterfaceName { get; } = interfaceName;
        public string? Member { get; } = member;
        public string? Sender { get; } = sender;
        public List<string> BodyStrings { get; } = bodyStrings;
    }

    private sealed class DbusWriter
    {
        private readonly List<byte> _bytes = [];
        private readonly Stack<int> _lengthAt = [];
        private readonly Stack<int> _bodyAt = [];
        public void Byte(byte value) => _bytes.Add(value);
        public void Boolean(bool value) { Align(4); WriteUInt32(value ? 1u : 0u); }
        public void UInt32(uint value) { Align(4); WriteUInt32(value); }
        public void Int64(long value)
        {
            Align(8);
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            foreach (var item in bytes) _bytes.Add(item);
        }
        public void Double(double value)
        {
            Align(8);
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
            foreach (var item in bytes) _bytes.Add(item);
        }
        public void String(string value)
        {
            Align(4);
            var utf8 = Encoding.UTF8.GetBytes(value);
            WriteUInt32((uint)utf8.Length);
            _bytes.AddRange(utf8);
            _bytes.Add(0);
        }
        public void Signature(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            _bytes.Add((byte)utf8.Length);
            _bytes.AddRange(utf8);
            _bytes.Add(0);
        }
        public void OpenArray(string _)
        {
            Align(4);
            _lengthAt.Push(_bytes.Count);
            WriteUInt32(0);
            Align(8);
            _bodyAt.Push(_bytes.Count);
        }
        public void CloseArray()
        {
            var body = _bodyAt.Pop();
            var lengthAt = _lengthAt.Pop();
            var length = (uint)(_bytes.Count - body);
            _bytes[lengthAt] = (byte)length;
            _bytes[lengthAt + 1] = (byte)(length >> 8);
            _bytes[lengthAt + 2] = (byte)(length >> 16);
            _bytes[lengthAt + 3] = (byte)(length >> 24);
        }
        public ReadOnlySpan<byte> AsSpan() => CollectionsMarshal.AsSpan(_bytes);
        public void Align(int align)
        {
            var pad = Pad(_bytes.Count, align);
            for (var i = 0; i < pad; i++) _bytes.Add(0);
        }
        private void WriteUInt32(uint value)
        {
            _bytes.Add((byte)value);
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)(value >> 16));
            _bytes.Add((byte)(value >> 24));
        }
    }
}
