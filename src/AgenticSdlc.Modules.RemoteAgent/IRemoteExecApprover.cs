// Approval gate for the remote dev-IDE agent runtime. Because a remote agent executes work on a
// developer machine, every dispatched request passes through an approver before it is pushed down
// the wire. Default auto-approves; a UI-driven human-in-the-loop approver replaces it later.

using System.Threading;
using System.Threading.Tasks;

namespace AgenticSdlc.Modules.RemoteAgent;

/// <summary>Decides whether a <see cref="RemoteExecRequest"/> may be sent to a remote agent.</summary>
public interface IRemoteExecApprover
{
    Task<bool> ApproveAsync(RemoteExecRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Default approver — allows everything.</summary>
public sealed class AutoApproveRemoteExec : IRemoteExecApprover
{
    public Task<bool> ApproveAsync(RemoteExecRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
