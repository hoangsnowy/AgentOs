// M3 — runner persistence contract, scoped to the current tenant by the DbContext query filter.
// Writes stamp TenantId from ITenantContext so a runner can't be created outside the caller's tenant.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Sessions.Persistence.Entities;

namespace AgentOs.Modules.Sessions.Persistence;

/// <summary>CRUD for runners (paired dev machines), scoped to the current tenant.</summary>
public interface IRunnerRepository
{
    Task<IReadOnlyList<RunnerEntity>> ListAsync(CancellationToken ct = default);
    Task<RunnerEntity?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(RunnerEntity runner, CancellationToken ct = default);

    /// <summary>Set a runner's status (e.g. revoke). Returns false if no such runner in this tenant.</summary>
    Task<bool> SetStatusAsync(Guid id, string status, CancellationToken ct = default);
}
