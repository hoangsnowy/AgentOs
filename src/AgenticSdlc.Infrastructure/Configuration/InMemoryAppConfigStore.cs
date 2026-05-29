// AgenticSdlc.Infrastructure/Configuration/InMemoryAppConfigStore.cs
// Phase 8.4 stop-gap — in-memory impl of IAppConfigStore. Keys survive only the current process
// lifetime. The production impl (forthcoming) stores entries in the app_config table, encrypts
// values via ASP.NET DataProtection, and refreshes a 15-second cache from the DB.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Configuration;

namespace AgenticSdlc.Infrastructure.Configuration;

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
        var keys = _items.Keys.Where(k => k.StartsWith(prefix, System.StringComparison.Ordinal)).OrderBy(k => k, System.StringComparer.Ordinal).ToList();
        return ValueTask.FromResult<IReadOnlyList<string>>(keys);
    }
}
