// AgenticSdlc.Infrastructure/Configuration/EfAppConfigStore.cs
// Phase 8.4b — persistent IAppConfigStore. Values are DataProtection-encrypted at rest in the
// app_config table. A 15-second read cache keeps the LLM hot path off the DB on every call.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Application.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Persistence.Entities;

namespace AgenticSdlc.Infrastructure.Configuration;

/// <summary>
/// EF-backed, DataProtection-encrypted <see cref="IAppConfigStore"/>. Singleton; opens a DbContext
/// scope per operation. Read path is cached for <see cref="CacheTtl"/> so the per-call LLM lookup
/// does not hit Postgres on every request.
/// </summary>
public sealed class EfAppConfigStore : IAppConfigStore
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;
    private readonly ILogger<EfAppConfigStore> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    /// <summary>Construct with a scope factory, a DataProtection provider, and a logger.</summary>
    public EfAppConfigStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider dp, ILogger<EfAppConfigStore> logger)
    {
        _scopeFactory = scopeFactory;
        _protector = dp.CreateProtector("AgenticSdlc.AppConfig.v1");
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var hit) && hit.FetchedUtc + CacheTtl > DateTime.UtcNow)
        {
            return hit.Value;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgenticSdlcDbContext>();
        var row = await db.AppConfig.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, cancellationToken).ConfigureAwait(false);
        var value = row is null ? null : TryUnprotect(row.EncryptedValue);
        _cache[key] = new CacheEntry(value, DateTime.UtcNow);
        return value;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgenticSdlcDbContext>();
        var cipher = _protector.Protect(value);
        var row = await db.AppConfig.FirstOrDefaultAsync(x => x.Key == key, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            db.AppConfig.Add(new AppConfigEntity { Key = key, EncryptedValue = cipher, UpdatedAtUtc = DateTime.UtcNow });
        }
        else
        {
            row.EncryptedValue = cipher;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _cache[key] = new CacheEntry(value, DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgenticSdlcDbContext>();
        var row = await db.AppConfig.FirstOrDefaultAsync(x => x.Key == key, cancellationToken).ConfigureAwait(false);
        if (row is not null)
        {
            db.AppConfig.Remove(row);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        _cache.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgenticSdlcDbContext>();
        var keys = await db.AppConfig.AsNoTracking()
            .Where(x => x.Key.StartsWith(prefix))
            .Select(x => x.Key)
            .OrderBy(k => k)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return keys;
    }

    private string? TryUnprotect(string cipher)
    {
        try
        {
            return _protector.Unprotect(cipher);
        }
        catch (Exception ex)
        {
            // Key ring rotated / corrupt ciphertext — log and treat as unset rather than crashing the run.
            _logger.LogWarning(ex, "Failed to decrypt an app_config value; treating as unset");
            return null;
        }
    }

    private readonly record struct CacheEntry(string? Value, DateTime FetchedUtc);
}
