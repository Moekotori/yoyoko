using Avalonia.Controls;
using Chat.Core.Sessions;
using Chat.Networking.Http;
using Chat.Protocol;
using Chat.Storage;
using Chat.UI.Shell;

internal static class LiveChatChecks
{
    public static void Run(Window window, ShellViewModel shell, string directory, string origin, Action<int> pump)
    {
        void Until(Func<bool> predicate, string name)
        {
            var end = Environment.TickCount64 + 10_000;
            while (!predicate() && Environment.TickCount64 < end) pump(10);
            if (!predicate()) throw new InvalidOperationException(name + ": " + shell.Status + " " + shell.Connection.Status);
        }
        T Await<T>(Task<T> task)
        {
            Until(() => task.IsCompleted, "HTTP/cache completion");
            return task.GetAwaiter().GetResult();
        }
        void AwaitVoid(Task task)
        {
            Until(() => task.IsCompleted, "HTTP/cache completion");
            task.GetAwaiter().GetResult();
        }
        void Require(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
        }

        Until(() => shell.IsSignedIn && shell.SelectedChannel?.Kind == "text" && !shell.IsChannelLoading && shell.InviteCode.Length > 0,
            "local desktop signup and channel ready");
        var session = shell.SelectedInstance!.Context.Session!;
        var channel = shell.SelectedChannel!.Id;
        var cache = new SqliteCache(Path.Combine(directory, "cache.db"));
        var nonce = Guid.NewGuid().ToString("N")[..10];
        using var peer = new ChatApiFactory().Create(new Uri(origin + "/api/v1"), new Uri(origin.Replace("http:", "ws:") + "/gateway"));
        var auth = Await(peer.RegisterAsync(new("peer_" + nonce, "Local test peer", Guid.NewGuid().ToString("N")), default));
        peer.SetAccessToken(auth.AccessToken);
        AwaitVoid(peer.JoinAsync(shell.InviteCode, default));
        var baseline = Await(peer.SendMessageAsync(channel, new("visible_" + nonce, null, []), Guid.NewGuid().ToString("N"), default));
        Until(() => shell.Messages.Any(row => row.Id == baseline.Id), "visible gateway delivery");
        var cursor = Await(cache.LoadCursorAsync(session.Scope, default));
        Require(cursor.SessionId is not null, "gateway has committed READY cursor");

        using var upload = new HeldUploadStream();
        AwaitVoid(shell.QueueFilesAsync([new PickedFile("ultralight.txt", "text/plain", upload, 3)]));
        shell.Draft = "upload_" + nonce;
        shell.Send.Execute(null);
        Until(() => upload.Started, "upload began before hiding");
        shell.Settings.UltraLightEnabled = true;
        window.WindowState = WindowState.Minimized;
        Require(shell.IsUltraLightParked && window.Content is null && !upload.Disposed, "parking retains the active upload stream");
        var hidden = Await(peer.SendMessageAsync(channel, new("hidden_" + nonce, null, []), Guid.NewGuid().ToString("N"), default));
        Until(() => cache.ReadPageAsync(session.Scope, channel, null, 50, default).GetAwaiter().GetResult().Items.Any(item => item.Id == hidden.Id),
            "hidden gateway event committed to SQLite");
        Until(() => shell.ChannelHasUnread, "hidden arrival remains unread");
        Require(shell.Messages.Count == 0, "background message does not rebuild unloaded UI");
        var after = Await(cache.LoadCursorAsync(session.Scope, default));
        Require(after.SessionId == cursor.SessionId && after.Seq > cursor.Seq, "existing gateway session advances while parked");

        upload.Release();
        Until(() => shell.Send.CanExecute(null), "pending send completed while parked");
        var remote = Await(peer.ListMessagesAsync(channel, null, 50, default));
        var sent = remote.Items.Single(item => item.Content == "upload_" + nonce);
        Require(sent.Attachments.Length == 1 && upload.Disposed, "upload finishes and is released only after send completion");
        using var downloaded = new MemoryStream();
        AwaitVoid(peer.DownloadToAsync(sent.Attachments[0].DownloadUrl, downloaded, 65536, default));
        Require(downloaded.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }), "peer downloads the complete attachment");

        window.WindowState = WindowState.Normal;
        Until(() => window.Content is ShellSurface && shell.Messages.Any(row => row.Id == hidden.Id)
            && shell.Messages.Any(row => row.Id == sent.Id), "restored timeline contains hidden receive and completed send");
        Require(shell.Messages.Count(row => row.Id == hidden.Id) == 1 && shell.Messages.Count(row => row.Id == sent.Id) == 1,
            "restore does not duplicate messages");
        Require(ReferenceEquals(shell.SelectedInstance!.Context.Session, session), "restore keeps the same account/session");
        Console.WriteLine("LIVE PASS: two local accounts, HTTP + Gateway + SQLite, hidden unread receive, in-flight upload/send/download and deduplicated restore. No voice hardware claim.");
    }
}

internal sealed class HeldUploadStream : Stream
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _position;
    public bool Started { get; private set; }
    public bool Disposed { get; private set; }
    public void Release() => _release.TrySetResult();
    public override bool CanRead => !Disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 3;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Started = true;
        await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(Disposed, this);
        var count = Math.Min(buffer.Length, 3 - _position);
        for (var i = 0; i < count; i++) buffer.Span[i] = (byte)(_position + i + 1);
        _position += count;
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use async upload.");
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}
