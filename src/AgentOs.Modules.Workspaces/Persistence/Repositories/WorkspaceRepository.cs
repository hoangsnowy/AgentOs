// EF-backed workspace repository. The DbContext global query filter enforces tenant isolation on the
// ambient-context reads; AddAsync stamps TenantId from ITenantContext so writes can't escape the
// tenant. The *ForTenant overloads use IgnoreQueryFilters + an explicit predicate for circuit callers.

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

    public async Task<WorkspaceEntity?> GetForTenantAsync(string tenantId, Guid id, CancellationToken ct = default)
    {
        return await _db.Workspaces
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == id, ct)
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

    // ── Repos under a board ──────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WorkspaceRepoEntity>> ListReposForTenantAsync(string tenantId, Guid workspaceId, CancellationToken ct = default)
    {
        return await _db.WorkspaceRepos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.WorkspaceId == workspaceId)
            .OrderBy(r => r.AddedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task AddRepoForTenantAsync(WorkspaceRepoEntity repo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repo);
        _db.WorkspaceRepos.Add(repo);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> RemoveRepoForTenantAsync(string tenantId, Guid repoId, CancellationToken ct = default)
    {
        var row = await _db.WorkspaceRepos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == repoId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }
        _db.WorkspaceRepos.Remove(row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
