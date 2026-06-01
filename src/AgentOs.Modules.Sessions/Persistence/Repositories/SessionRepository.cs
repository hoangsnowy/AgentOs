// EF-backed session repository. The DbContext global query filter enforces tenant isolation on reads;
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

internal sealed class SessionRepository : ISessionRepository
{
    private readonly SessionsDbContext _db;
    private readonly ITenantContext _tenant;

    public SessionRepository(SessionsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<RemoteSessionEntity>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Sessions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<RemoteSessionEntity?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(RemoteSessionEntity session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.TenantId = _tenant.TenantId;
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> CloseAsync(Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default)
    {
        var row = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = "Closed";
        row.ClosedAtUtc = closedAtUtc;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<RemoteSessionEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return await _db.Sessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddForTenantAsync(RemoteSessionEntity session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> CloseForTenantAsync(string tenantId, Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default)
    {
        var row = await _db.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = "Closed";
        row.ClosedAtUtc = closedAtUtc;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> UpdateRunResultAsync(
        string tenantId, Guid id, string status, string? prUrl, string? errorMessage,
        CancellationToken ct = default)
    {
        var row = await _db.Sessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        row.Status = status;
        row.PrUrl = prUrl;
        row.Error = errorMessage;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
