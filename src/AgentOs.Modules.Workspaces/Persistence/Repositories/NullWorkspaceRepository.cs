// No-op workspace repository used when no database is configured (CI / local stateless runs). It
// satisfies the DI contract so the host boots, but persists nothing and returns empty history.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Workspaces.Persistence.Entities;

namespace AgentOs.Modules.Workspaces.Persistence.Repositories;

internal sealed class NullWorkspaceRepository : IWorkspaceRepository
{
    public Task<IReadOnlyList<WorkspaceEntity>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WorkspaceEntity>>(Array.Empty<WorkspaceEntity>());

    public Task<WorkspaceEntity?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<WorkspaceEntity?>(null);

    public Task AddAsync(WorkspaceEntity workspace, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<WorkspaceEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WorkspaceEntity>>(Array.Empty<WorkspaceEntity>());

    public Task AddForTenantAsync(WorkspaceEntity workspace, CancellationToken ct = default)
        => Task.CompletedTask;
}
