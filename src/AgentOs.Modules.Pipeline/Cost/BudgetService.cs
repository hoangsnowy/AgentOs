// Tenant-explicit budget read/write. Centralises the AppConfig key strings + invariant formatting so the
// Cost app component stays declarative. Reads delegate to the IBudgetGuard (single source of the math).

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Cost;
using AgentOs.Modules.AppConfig;

namespace AgentOs.Modules.Pipeline.Cost;

internal sealed class BudgetService : IBudgetService
{
    private readonly IAppConfigStore _config;
    private readonly IBudgetGuard _guard;

    public BudgetService(IAppConfigStore config, IBudgetGuard guard)
    {
        _config = config ?? throw new System.ArgumentNullException(nameof(config));
        _guard = guard ?? throw new System.ArgumentNullException(nameof(guard));
    }

    public Task<BudgetStatus> GetAsync(string tenantId, CancellationToken cancellationToken = default)
        => _guard.EvaluateAsync(tenantId, cancellationToken);

    public async Task SetCapAsync(string tenantId, decimal capUsd, CancellationToken cancellationToken = default)
    {
        if (capUsd <= 0m)
        {
            await _config.DeleteForTenantAsync(tenantId, BudgetGuard.CapKey, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _config.SetForTenantAsync(
            tenantId, BudgetGuard.CapKey, capUsd.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEnforceAsync(string tenantId, bool enabled, CancellationToken cancellationToken = default)
        => await _config.SetForTenantAsync(
            tenantId, BudgetGuard.EnforceKey, enabled ? "true" : "false", cancellationToken).ConfigureAwait(false);
}
