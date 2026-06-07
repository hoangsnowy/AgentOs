// M3 — runner persistence contract, scoped to the current tenant by the DbContext query filter.
// Writes stamp TenantId from ITenantContext so a runner can't be created outside the caller's tenant.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Sessions.Persistence.Entities;
using AgentOs.SharedKernel.Persistence;

namespace AgentOs.Modules.Sessions.Persistence;

/// <summary>CRUD for runners (paired dev machines), scoped to the current tenant.</summary>
public interface IRunnerRepository
{
    /// <summary>List the current tenant's runners, newest first, bounded by offset/limit pagination.</summary>
    Task<IReadOnlyList<RunnerEntity>> ListAsync(int limit = Page.DefaultLimit, int offset = 0, CancellationToken ct = default);
    Task<RunnerEntity?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(RunnerEntity runner, CancellationToken ct = default);

    /// <summary>Set a runner's status (e.g. revoke). Returns false if no such runner in this tenant.</summary>
    Task<bool> SetStatusAsync(Guid id, string status, CancellationToken ct = default);

    // ---- Tenant-explicit overloads: bypass the ambient query filter for callers without an
    // ITenantContext (a Blazor Server circuit has no HttpContext). Pass the tenant id from the
    // signed-in principal; the entity passed to AddForTenantAsync must already carry its TenantId. ----

    Task<IReadOnlyList<RunnerEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default);
    Task AddForTenantAsync(RunnerEntity runner, CancellationToken ct = default);
    Task<bool> SetStatusForTenantAsync(string tenantId, Guid id, string status, CancellationToken ct = default);
}
