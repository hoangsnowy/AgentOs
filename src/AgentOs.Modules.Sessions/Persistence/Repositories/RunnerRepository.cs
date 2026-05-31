// EF-backed runner repository. The DbContext global query filter enforces tenant isolation on reads;
// AddAsync stamps TenantId from ITenantContext so writes can't escape the tenant.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Sessions.Persistence.Repositories;

internal sealed class RunnerRepository : IRunnerRepository
{
    private readonly SessionsDbContext _db;
    private readonly ITenantContext _tenant;

    public RunnerRepository(SessionsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<RunnerEntity>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Runners
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<RunnerEntity?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Runners
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);
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
        return await _db.Runners
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
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
