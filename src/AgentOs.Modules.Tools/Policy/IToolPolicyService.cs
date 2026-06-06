// Tenant-explicit read/write of a tenant's tool allowlist. Used by the admin Policy app (a Blazor
// circuit with no HttpContext), so the tenant id is passed explicitly. Backed by the AppConfig KV store.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.Tools.Policy;

/// <summary>A tenant's tool-governance settings.</summary>
/// <param name="Enforce">When true, only <see cref="AllowedTools"/> may be invoked.</param>
/// <param name="AllowedTools">Allowed tool names (meaningful only when <see cref="Enforce"/> is true).</param>
public sealed record ToolPolicyState(bool Enforce, IReadOnlyList<string> AllowedTools);

/// <summary>Reads + writes a tenant's tool allowlist.</summary>
public interface IToolPolicyService
{
    /// <summary>The tenant's current enforce flag + allowlist.</summary>
    Task<ToolPolicyState> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Set the tenant's enforce flag + allowlist.</summary>
    Task SetAsync(string tenantId, bool enforce, IEnumerable<string> allowedTools, CancellationToken cancellationToken = default);
}
