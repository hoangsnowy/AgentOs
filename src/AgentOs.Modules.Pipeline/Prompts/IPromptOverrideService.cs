// Tenant-explicit read/write of an agent's system-prompt override. Used by the admin Prompts app (a
// Blazor circuit with no HttpContext), so the tenant id is passed explicitly. Backed by AppConfig.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.AppConfig;

namespace AgentOs.Modules.Pipeline.Prompts;

/// <summary>Reads + writes a tenant's per-agent system-prompt override.</summary>
public interface IPromptOverrideService
{
    /// <summary>The tenant's override for the agent, or null when none is set.</summary>
    Task<string?> GetAsync(string tenantId, string agentKey, CancellationToken cancellationToken = default);

    /// <summary>Sets the override (blank clears it back to the default).</summary>
    Task SetAsync(string tenantId, string agentKey, string prompt, CancellationToken cancellationToken = default);

    /// <summary>Clears the override (revert to the compiled-in default).</summary>
    Task ClearAsync(string tenantId, string agentKey, CancellationToken cancellationToken = default);
}

internal sealed class PromptOverrideService : IPromptOverrideService
{
    private readonly IAppConfigStore _config;

    public PromptOverrideService(IAppConfigStore config)
        => _config = config ?? throw new System.ArgumentNullException(nameof(config));

    public async Task<string?> GetAsync(string tenantId, string agentKey, CancellationToken cancellationToken = default)
        => await _config.GetForTenantAsync(tenantId, AppConfigPromptOverrides.Key(agentKey), cancellationToken).ConfigureAwait(false);

    public async Task SetAsync(string tenantId, string agentKey, string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            await ClearAsync(tenantId, agentKey, cancellationToken).ConfigureAwait(false);
            return;
        }
        await _config.SetForTenantAsync(tenantId, AppConfigPromptOverrides.Key(agentKey), prompt, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(string tenantId, string agentKey, CancellationToken cancellationToken = default)
        => await _config.DeleteForTenantAsync(tenantId, AppConfigPromptOverrides.Key(agentKey), cancellationToken).ConfigureAwait(false);
}
