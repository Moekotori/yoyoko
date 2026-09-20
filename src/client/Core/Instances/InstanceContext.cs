using Chat.Domain.Instances;
using Chat.Core.Realtime;

namespace Chat.Core.Instances;

// One owner per independent instance. No global account or global gateway.
public sealed class InstanceContext(InstanceDescriptor descriptor) : IAsyncDisposable
{
    public InstanceDescriptor Descriptor { get; } = descriptor;
    public Account? Account { get; private set; }
    public IGatewayConnection? Gateway { get; private set; }
    public void AttachSession(Account account, IGatewayConnection gateway)
    {
        if (account.Key.InstanceId != Descriptor.Id) throw new ArgumentException("Instance mismatch.");
        if (Gateway is not null) throw new InvalidOperationException("Dispose the previous session first.");
        Account = account;
        Gateway = gateway;
    }
    public async ValueTask DisposeAsync()
    {
        if (Gateway is not null) await Gateway.DisposeAsync();
        Gateway = null;
        Account = null;
    }
}
