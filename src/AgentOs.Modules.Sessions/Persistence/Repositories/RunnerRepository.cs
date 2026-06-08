// Runner repository. Reads run as Dapper SQL when a raw connection is wired (INpgsqlConnectionFactory),
// else EF fallback. EF owns writes. TENANT SAFETY: the EF global query filter does not apply on the
// Dapper path, so every Dapper read carries an explicit `WHERE "TenantId" = @tenantId`. Columns are
// PascalCase (quoted); table is sessions.runners.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Sessions.Persistence.Repositories;

internal sealed class RunnerRepository : IRunnerRepository
{
    private readonly SessionsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly INpgsqlConnectionFactory? _conn;

    public RunnerRepository(SessionsDbContext db, ITenantContext tenant, INpgsqlConnectionFactory? conn = null)
    {
        _db = db;
        _tenant = tenant;
        _conn = conn;
    }

    public async Task<IReadOnlyList<RunnerEntity>> ListAsync(int limit = Page.DefaultLimit, int offset = 0, CancellationToken ct = default)
    {
        var lim = Page.ClampLimit(limit);
        var off = Page.ClampOffset(offset);
        if (_conn is null)
        {
            return await _db.Runners
                .AsNoTracking()
                .OrderByDescending(r => r.CreatedAtUtc)
                .Skip(off)
                .Take(lim)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        const string sql = """
            SELECT * FROM sessions.runners
            WHERE "TenantId" = @tenantId
            ORDER BY "CreatedAtUtc" DESC
            LIMIT @lim OFFSET @off
            """;
        return await QueryRunnersAsync(sql, new { tenantId = _tenant.TenantId, lim, off }, ct).ConfigureAwait(false);
    }

    public async Task<RunnerEntity?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (_conn is null)
        {
            return await _db.Runners
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, ct)
                .ConfigureAwait(false);
        }

        const string sql = """SELECT * FROM sessions.runners WHERE "Id" = @id AND "TenantId" = @tenantId""";
        await using var conn = _conn.Create();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QueryFirstOrDefaultAsync<RunnerEntity>(
            new CommandDefinition(sql, new { id, tenantId = _tenant.TenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task AddAsync(RunnerEntity runner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        runner.TenantId = _tenant.TenantId;
        _db.Runners.Add(runner);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> SetStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var row = await _db.Runners.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = status;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<RunnerEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        if (_conn is null)
        {
            return await _db.Runners
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.TenantId == tenantId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        const string sql = """
            SELECT * FROM sessions.runners
            WHERE "TenantId" = @tenantId
            ORDER BY "CreatedAtUtc" DESC
            """;
        return await QueryRunnersAsync(sql, new { tenantId }, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<RunnerEntity>> QueryRunnersAsync(string sql, object parms, CancellationToken ct)
    {
        await using var conn = _conn!.Create();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RunnerEntity>(
            new CommandDefinition(sql, parms, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task AddForTenantAsync(RunnerEntity runner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _db.Runners.Add(runner);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> SetStatusForTenantAsync(string tenantId, Guid id, string status, CancellationToken ct = default)
    {
        var row = await _db.Runners
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = status;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
