// Cross-process startup coordination. Every module runs EF MigrateAsync at boot; on a multi-replica
// deployment (ACA rolling update, scale-out) two replicas would race the same migration — EF's
// migration runner is not designed for concurrent writers and can deadlock or partially apply.
// A Postgres session-level advisory lock serialises the migrating replica; the others wait, then
// see an already-migrated schema and no-op.

using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace AgentOs.SharedKernel.Persistence;

/// <summary>A held <c>pg_advisory_lock</c> on a dedicated connection. Dispose releases the lock
/// (closing the session releases it even on crash).</summary>
public sealed class PgAdvisoryLock : IAsyncDisposable
{
    private readonly NpgsqlConnection? _connection;
    private readonly long _key;

    /// <summary>Default bound on how long <see cref="AcquireAsync"/> waits for the lock before failing fast.</summary>
    private static readonly TimeSpan DefaultAcquireTimeout = TimeSpan.FromSeconds(60);

    private PgAdvisoryLock(NpgsqlConnection? connection, long key)
    {
        _connection = connection;
        _key = key;
    }

    /// <summary>Acquires <c>pg_advisory_lock(key)</c> for <paramref name="lockName"/> (stable FNV-1a
    /// hash), blocking until the holder releases it or the acquire timeout (<see cref="DefaultAcquireTimeout"/>,
    /// 60s) elapses. A null/empty <paramref name="connectionString"/> (no real database — InMemory tests,
    /// no-op repos) returns a no-op handle. Throws <see cref="TimeoutException"/> if the wait is bounded out —
    /// fail fast so the orchestrator restarts the pod rather than wedging startup forever.</summary>
    public static async Task<PgAdvisoryLock> AcquireAsync(
        string? connectionString, string lockName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockName);
        var key = Fnv1aHash(lockName);

        if (string.IsNullOrWhiteSpace(connectionString)
            || !connectionString.Contains('=', StringComparison.Ordinal))
        {
            // No real Postgres behind this context (tests / stateless boot) — nothing to serialise.
            return new PgAdvisoryLock(null, key);
        }

        // pg_advisory_lock() blocks server-side with NO timeout: a holder that crashed mid-migration (or an
        // unreachable Postgres) would hang every other replica's startup indefinitely while its health probe
        // still reports 200. Bound the wait — on expiry Npgsql cancels the command and we surface a clear
        // TimeoutException so the failure is observable and the pod is recycled instead of silently stuck.
        var timeout = DefaultAcquireTimeout;
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(linked.Token).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteNonQueryAsync(linked.Token).ConfigureAwait(false);
            return new PgAdvisoryLock(connection, key);
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Our timeout fired (not the caller's token) — translate whatever Npgsql threw on cancel into a
            // clear, actionable timeout. The caller-cancellation case falls through to the generic rethrow.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0}s acquiring the '{lockName}' migration advisory lock. "
                + "Another replica may be stuck mid-migration, or Postgres is unreachable.", ex);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Stable 64-bit FNV-1a of the lock name — deterministic across processes/restarts
    /// (string.GetHashCode is randomized per process and must not be used here).</summary>
    internal static long Fnv1aHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return (long)hash;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", _connection);
            command.Parameters.AddWithValue("key", _key);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            // Connection already broken — closing it below releases the session lock server-side.
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
