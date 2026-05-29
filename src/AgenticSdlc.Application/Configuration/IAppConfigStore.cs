// AgenticSdlc.Application/Configuration/IAppConfigStore.cs
// Phase 8.4 — Runtime-mutable configuration store. Lets the API rotate LLM keys, JWT secrets,
// and other operator-controlled settings without restarting. The interface is intentionally
// boring (string key → string value); concrete impls add encryption + TTL caching.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AgenticSdlc.Application.Configuration;

/// <summary>Key-value store for runtime-mutable application configuration (LLM keys, JWT secret, …).</summary>
public interface IAppConfigStore
{
    /// <summary>Read a value by key. <c>null</c> when not set.</summary>
    /// <param name="key">Configuration key (e.g. <c>Llm:Claude:ApiKey</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Write a value by key. Overwrites the previous value if present.</summary>
    /// <param name="key">Configuration key.</param>
    /// <param name="value">New value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Delete a value by key. No-op when missing.</summary>
    /// <param name="key">Configuration key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>List keys with a given prefix. Useful for the Settings UI to enumerate <c>Llm:*</c>.</summary>
    /// <param name="prefix">Key prefix (e.g. <c>Llm:</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default);
}
