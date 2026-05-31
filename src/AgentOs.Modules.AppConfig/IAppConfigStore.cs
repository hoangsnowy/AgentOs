// Runtime-mutable configuration store. Lets the API rotate LLM keys, JWT secrets, and other
// operator-controlled settings without restarting. The interface is intentionally boring (string
// key → string value); concrete impls add encryption + TTL caching.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.AppConfig;

/// <summary>Key-value store for runtime-mutable application configuration (LLM keys, JWT secret, …).</summary>
public interface IAppConfigStore
{
    /// <summary>Read a value by key. <c>null</c> when not set.</summary>
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Write a value by key. Overwrites the previous value if present.</summary>
    ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Delete a value by key. No-op when missing.</summary>
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>List keys with a given prefix. Useful for the Settings UI to enumerate <c>Llm:*</c>.</summary>
    ValueTask<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Write a value for an explicit tenant. For callers without an ambient <c>ITenantContext</c>
    /// (a Blazor Server circuit has no HttpContext, and this store resolves the tenant from a fresh
    /// scope that would fall back to the default tenant). Pass the tenant from the signed-in principal.</summary>
    ValueTask SetForTenantAsync(string tenantId, string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Delete a value for an explicit tenant. See <see cref="SetForTenantAsync"/>.</summary>
    ValueTask DeleteForTenantAsync(string tenantId, string key, CancellationToken cancellationToken = default);
}
