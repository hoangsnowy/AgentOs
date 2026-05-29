// CRUD for the tenant registry. Reads + writes span every tenant (the registry is admin-scoped,
// not request-scoped), so callers must guard endpoints behind the Admin policy.

namespace AgenticSdlc.Modules.Tenants;

/// <summary>Tenant registry (display metadata + provisioning timestamp). The OIDC token's
/// <c>tenant</c> claim — not this table — is the runtime authority on tenant membership.</summary>
public interface ITenantsRepository
{
    /// <summary>List every tenant ordered by Id.</summary>
    System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<TenantRecord>> ListAsync(System.Threading.CancellationToken ct = default);

    /// <summary>Fetch one tenant by Id, or null if not found.</summary>
    System.Threading.Tasks.Task<TenantRecord?> GetAsync(string id, System.Threading.CancellationToken ct = default);

    /// <summary>Insert a new tenant; throws if the Id already exists.</summary>
    System.Threading.Tasks.Task AddAsync(TenantRecord tenant, System.Threading.CancellationToken ct = default);
}

/// <summary>One tenant record from the registry.</summary>
public sealed record TenantRecord(string Id, string Name, System.DateTimeOffset CreatedAtUtc);
