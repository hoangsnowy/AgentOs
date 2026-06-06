// Tenant-explicit read/write of a tenant's LLM budget cap. Used by the Cost desktop app (a Blazor
// circuit with no HttpContext), so every method takes the tenant id explicitly. Reads delegate to the
// IBudgetGuard; writes go to the encrypted AppConfig KV store.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Cost;

namespace AgentOs.Modules.Pipeline.Cost;

/// <summary>Reads + writes a tenant's monthly LLM budget cap and enforce flag.</summary>
public interface IBudgetService
{
    /// <summary>The tenant's current budget status (cap, spend, state).</summary>
    Task<BudgetStatus> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Sets the monthly cap (USD). A value &lt;= 0 clears the cap (back to unconstrained).</summary>
    Task SetCapAsync(string tenantId, decimal capUsd, CancellationToken cancellationToken = default);

    /// <summary>Turns hard enforcement on/off (when enabled, over-cap runs are blocked).</summary>
    Task SetEnforceAsync(string tenantId, bool enabled, CancellationToken cancellationToken = default);
}
