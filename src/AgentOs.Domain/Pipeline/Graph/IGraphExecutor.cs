// The host-facing facade over the graph executor. Hosts (Web / Api) depend on this Domain interface, not on
// the Pipeline-module concrete GraphExecutor — so the executor's MAF/agent wiring stays internal to the module.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Domain.Pipeline.Graph;

/// <summary>Validates (LLM-free) then compiles + runs a <see cref="PlanGraph"/>, pushing per-node status to
/// <paramref name="onNode"/>. <paramref name="tenantId"/> + <paramref name="userId"/> are passed explicitly
/// because a Blazor circuit has no <c>ITenantContext</c>.</summary>
public interface IGraphExecutor
{
    Task<GraphRunResult> RunAsync(PlanGraph graph, string userStoryText, int nMax, string tenantId, string? userId,
        Func<GraphNodeEvent, Task> onNode, Func<GraphHumanRequest, Task<GraphHumanReply>>? onHuman = null, CancellationToken ct = default);
}
