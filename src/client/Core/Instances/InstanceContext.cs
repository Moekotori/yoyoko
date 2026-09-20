using Chat.Core.Realtime;
using Chat.Core.Sessions;
using Chat.Domain.Instances;

namespace Chat.Core.Instances;

public sealed class InstanceContext(InstanceDescriptor descriptor) : IAsyncDisposable
{
    public InstanceDescriptor Descriptor { get; } = descriptor;
    public InstanceSession? Session { get; private set; }
    public Account? Account => Session?.Account;
    public IGatewayConnection? Gateway => null;
    public void AttachSession(InstanceSession session)
    {
        if (session.Descriptor.Id != Descriptor.Id) throw new ArgumentException("Instance mismatch.");
        if (Session is not null) throw new InvalidOperationException("Dispose the previous session first.");
        Session = session;
    }
    public void AttachSessionClear()
    {
        Session = null;
    }
    public async ValueTask DisposeAsync()
    {
        if (Session is not null) await Session.DisposeAsync();
        Session = null;
    }
}
