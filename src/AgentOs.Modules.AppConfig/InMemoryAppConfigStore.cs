// Process-scoped in-memory IAppConfigStore. Keys survive only the current process lifetime.
// Fallback when no DB is configured.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AgentOs.Modules.AppConfig;

/// <summary>Process-scoped in-memory <see cref="IAppConfigStore"/>. Singleton.</summary>
public sealed class InMemoryAppConfigStore : IAppConfigStore
{
    private readonly ConcurrentDictionary<string, string> _items = new();

    /// <inheritdoc />
    public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_items.TryGetValue(key, out var v) ? v : null);

    /// <inheritdoc />
    public ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _items[key] = value;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _items.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var keys = _items.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        return ValueTask.FromResult<IReadOnlyList<string>>(keys);
    }

    // This fallback store is already tenant-agnostic (keys are globally unique, e.g. workspace/{id}/token),
    // so the tenant-explicit overloads just delegate.

    /// <inheritdoc />
    public ValueTask SetForTenantAsync(string tenantId, string key, string value, CancellationToken cancellationToken = default)
        => SetAsync(key, value, cancellationToken);

    /// <inheritdoc />
    public ValueTask DeleteForTenantAsync(string tenantId, string key, CancellationToken cancellationToken = default)
        => DeleteAsync(key, cancellationToken);
}
