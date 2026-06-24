// Approval gate for the remote dev-IDE agent runtime. Because a remote agent executes work on a
// developer machine, every dispatched request passes through an approver before it is pushed down
// the wire. The registered default is ConfigGatedRemoteExecApprover (deny outside Development unless
// RemoteAgent:AutoApprove=true); a UI-driven human-in-the-loop approver replaces it later.

using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.RemoteAgent;

/// <summary>Decides whether work may be sent to a remote agent — both the full-prompt dispatch (M3)
/// and the per-tool-call dispatch (M4, e.g. <c>runner_shell</c> running an arbitrary command on the
/// paired machine). Both paths MUST gate here so a future human-in-the-loop approver covers them.</summary>
public interface IRemoteExecApprover
{
    Task<bool> ApproveAsync(RemoteExecRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gate for a single tool call pushed to the runner (the <c>runner_shell</c> path).</summary>
    Task<bool> ApproveToolCallAsync(RunnerToolCall toolCall, CancellationToken cancellationToken = default);
}

/// <summary>Permissive approver — allows everything. No longer the registered default (see
/// <see cref="ConfigGatedRemoteExecApprover"/>); retained for tests and explicit dev wiring.</summary>
public sealed class AutoApproveRemoteExec : IRemoteExecApprover
{
    public Task<bool> ApproveAsync(RemoteExecRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<bool> ApproveToolCallAsync(RunnerToolCall toolCall, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
