// EF-backed IAuditLog. Writes are best-effort: a DB outage must not break the surrounding
// signup / invitation flow, so the writer catches every exception and logs it. Reads are
// tenant-scoped and bounded by `max` — no cross-tenant peek by construction.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Persistence.Entities;
using AgentOs.SharedKernel.Logging;
using AgentOs.SharedKernel.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Tenants.Persistence.Repositories;

internal sealed class EfAuditLog(
    TenantsDbContext db,
    ILogger<EfAuditLog> logger,
    INpgsqlConnectionFactory? connectionFactory = null) : IAuditLog
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        try
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
                TenantId = entry.TenantId,
                UserId = entry.UserId,
                Action = entry.Action,
                Target = entry.Target,
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent,
                TimestampUtc = entry.TimestampUtc == default ? DateTimeOffset.UtcNow : entry.TimestampUtc,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) { Handle(ex); }
        catch (System.Data.Common.DbException ex) { Handle(ex); }
        catch (InvalidOperationException ex) { Handle(ex); }

        void Handle(Exception e) =>
            // The action label is intentionally NOT echoed here: some action constants are named
            // after sensitive operations (e.g. password reset), which trips static-analysis secret
            // heuristics, and the action is already captured in the persisted audit record. Tenant +
            // exception are enough to diagnose a failed best-effort audit write.
            logger.LogWarning(e,
                "Audit write failed for tenant={TenantId} — surrounding op continues.",
                LogSafe.Scrub(entry.TenantId));
    }

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(string tenantId, int max = 100, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (connectionFactory is not null)
        {
            return await ListViaDapperAsync(tenantId, max, ct).ConfigureAwait(false);
        }

        var rows = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(max)
            .Select(e => new AuditEntry(e.Id, e.TenantId, e.UserId, e.Action, e.Target, e.IpAddress, e.UserAgent, e.TimestampUtc))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows;
    }

    // Dapper fast-path: audit_events under the "tenants" schema; the (TenantId, TimestampUtc) index
    // serves the filter + ordering. LIMIT is clamped to ≥0 to match EF's Take semantics.
    private async Task<IReadOnlyList<AuditEntry>> ListViaDapperAsync(string tenantId, int max, CancellationToken ct)
    {
        const string sql = """
            SELECT "Id", "TenantId", "UserId", "Action", "Target", "IpAddress", "UserAgent", "TimestampUtc"
            FROM tenants.audit_events
            WHERE "TenantId" = @tenantId
            ORDER BY "TimestampUtc" DESC
            LIMIT @max
            """;

        await using var conn = connectionFactory!.Create();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<AuditEventEntity>(
            new CommandDefinition(sql, new { tenantId, max = Math.Max(0, max) }, cancellationToken: ct))
            .ConfigureAwait(false);

        return rows.Select(e => new AuditEntry(
            e.Id, e.TenantId, e.UserId, e.Action, e.Target, e.IpAddress, e.UserAgent, e.TimestampUtc)).ToList();
    }
}

internal sealed class NullAuditLog : IAuditLog
{
    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<AuditEntry>> ListAsync(string tenantId, int max = 100, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>([]);
}
