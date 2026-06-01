// M3 — session persistence contract, scoped to the current tenant by the DbContext query filter.
// Writes stamp TenantId from ITenantContext.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Sessions.Persistence.Entities;

namespace AgentOs.Modules.Sessions.Persistence;

/// <summary>CRUD for remote sessions (member × workspace), scoped to the current tenant.</summary>
public interface ISessionRepository
{
    Task<IReadOnlyList<RemoteSessionEntity>> ListAsync(CancellationToken ct = default);
    Task<RemoteSessionEntity?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(RemoteSessionEntity session, CancellationToken ct = default);

    /// <summary>Mark a session Closed with the given timestamp. Returns false if not found in this tenant.</summary>
    Task<bool> CloseAsync(Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default);

    // ---- Tenant-explicit overloads: bypass the ambient query filter for callers without an
    // ITenantContext (a Blazor Server circuit has no HttpContext). The entity passed to
    // AddForTenantAsync must already carry its TenantId. ----

    Task<IReadOnlyList<RemoteSessionEntity>> ListForTenantAsync(string tenantId, CancellationToken ct = default);
    Task AddForTenantAsync(RemoteSessionEntity session, CancellationToken ct = default);
    Task<bool> CloseForTenantAsync(string tenantId, Guid id, DateTimeOffset closedAtUtc, CancellationToken ct = default);

    /// <summary>Updates status + run-result fields after the issue-work agent completes (M5).
    /// Bypasses the ambient query filter — tenant must be supplied explicitly.</summary>
    Task<bool> UpdateRunResultAsync(
        string tenantId, Guid id, string status, string? prUrl, string? errorMessage,
        CancellationToken ct = default);
}
