// Epic E5 — Default policy. Allows every invocation. Replace with a tenant-aware policy by
// registering an alternative IToolPolicy in DI; the gateway resolves the registered impl.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;

namespace AgentOs.Modules.Tools.Policy;

internal sealed class PermissiveToolPolicy : IToolPolicy
{
    public Task<ToolPolicyDecision> EvaluateAsync(
        ToolInvocationRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ToolPolicyDecision.Allow);
}
