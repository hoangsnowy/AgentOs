// Tenant-explicit allowlist read/write. Centralises the AppConfig key strings + the CSV encoding so the
// Policy app stays declarative.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.AppConfig;

namespace AgentOs.Modules.Tools.Policy;

internal sealed class ToolPolicyService : IToolPolicyService
{
    private readonly IAppConfigStore _config;

    public ToolPolicyService(IAppConfigStore config)
        => _config = config ?? throw new ArgumentNullException(nameof(config));

    public async Task<ToolPolicyState> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var enforce = await _config.GetForTenantAsync(tenantId, AppConfigToolPolicy.EnforceKey, cancellationToken).ConfigureAwait(false);
        var raw = await _config.GetForTenantAsync(tenantId, AppConfigToolPolicy.AllowlistKey, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var allowed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new ToolPolicyState(string.Equals(enforce, "true", StringComparison.OrdinalIgnoreCase), allowed);
    }

    public async Task SetAsync(string tenantId, bool enforce, IEnumerable<string> allowedTools, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedTools);
        var csv = string.Join(',', allowedTools.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        await _config.SetForTenantAsync(tenantId, AppConfigToolPolicy.AllowlistKey, csv, cancellationToken).ConfigureAwait(false);
        await _config.SetForTenantAsync(tenantId, AppConfigToolPolicy.EnforceKey, enforce ? "true" : "false", cancellationToken).ConfigureAwait(false);
    }
}
