// EF-backed workspace repository. The DbContext global query filter enforces tenant isolation on
// reads; AddAsync stamps TenantId from ITenantContext so writes can't escape the tenant.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Workspaces.Persistence.Entities;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Workspaces.Persistence.Repositories;

internal sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly WorkspacesDbContext _db;
    private readonly ITenantContext _tenant;

    public WorkspaceRepository(WorkspacesDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<WorkspaceEntity>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Workspaces
            .AsNoTracking()
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<WorkspaceEntity?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(WorkspaceEntity workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.TenantId = _tenant.TenantId;
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkspaceEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return await _db.Workspaces
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddForTenantAsync(WorkspaceEntity workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        _db.Workspaces.Remove(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
