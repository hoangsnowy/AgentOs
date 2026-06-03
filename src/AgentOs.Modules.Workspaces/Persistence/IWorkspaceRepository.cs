// M2 / board reshape — workspace persistence contract. Tenant scoping is enforced by the DbContext
// global query filter, so the ambient-context methods never pass a tenant id; the *ForTenant
// overloads are for callers without an ITenantContext (a Blazor Server circuit has no HttpContext)
// and pass the tenant id explicitly, read from the signed-in principal.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Workspaces.Persistence.Entities;

namespace AgentOs.Modules.Workspaces.Persistence;

/// <summary>CRUD for connected boards + the repos under them, scoped to the current tenant.</summary>
public interface IWorkspaceRepository
{
    Task<IReadOnlyList<WorkspaceEntity>> ListAsync(CancellationToken ct = default);
    Task<WorkspaceEntity?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(WorkspaceEntity workspace, CancellationToken ct = default);
    Task<bool> RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>List a specific tenant's boards, bypassing the ambient query filter (circuit callers).</summary>
    Task<IReadOnlyList<WorkspaceEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default);

    /// <summary>Get a board by id for an explicit tenant (circuit callers). Null if not found in that tenant.</summary>
    Task<WorkspaceEntity?> GetForTenantAsync(string tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Persist a board whose <see cref="WorkspaceEntity.TenantId"/> is already set by the caller.</summary>
    Task AddForTenantAsync(WorkspaceEntity workspace, CancellationToken ct = default);

    // ── Repos under a board ──────────────────────────────────────────────────────────────────────

    /// <summary>List the repos connected under a board, for an explicit tenant.</summary>
    Task<IReadOnlyList<WorkspaceRepoEntity>> ListReposForTenantAsync(string tenantId, Guid workspaceId, CancellationToken ct = default);

    /// <summary>Persist a repo under a board (its <see cref="WorkspaceRepoEntity.TenantId"/> is set by the caller).</summary>
    Task AddRepoForTenantAsync(WorkspaceRepoEntity repo, CancellationToken ct = default);

    /// <summary>Remove a repo under a board, scoped to an explicit tenant. Returns false if not found.</summary>
    Task<bool> RemoveRepoForTenantAsync(string tenantId, Guid repoId, CancellationToken ct = default);
}
