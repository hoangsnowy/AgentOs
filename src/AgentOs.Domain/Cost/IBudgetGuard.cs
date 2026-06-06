// The budget gate. A run entrypoint calls EvaluateAsync before doing expensive LLM work; the host
// supplies an implementation that reads the per-tenant cap + month-to-date spend.

using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Domain.Cost;

/// <summary>Evaluates a tenant's month-to-date LLM spend against its configured budget cap.</summary>
public interface IBudgetGuard
{
    /// <summary>Returns the tenant's current <see cref="BudgetStatus"/>. Tenant-explicit so it is safe to
    /// call from a background task / Blazor circuit where the ambient tenant context is blank.</summary>
    Task<BudgetStatus> EvaluateAsync(string tenantId, CancellationToken cancellationToken = default);
}
