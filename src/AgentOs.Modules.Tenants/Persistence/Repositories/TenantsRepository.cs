// Tenant registry. Spans every tenant — this table IS the tenant list, so (unlike tenant-scoped repos)
// its reads carry NO tenant filter; admin-policy guards live in the API endpoints. Reads run as Dapper
// SQL over a raw connection (Dapper-only — no EF read fallback); EF owns writes. The real repo is
// registered only with a connection string (else NullTenantsRepository), so the factory is always
// present in production; a null factory throws. Columns are PascalCase (quoted); table is tenants.tenants.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Persistence.Entities;
using AgentOs.SharedKernel.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Tenants.Persistence.Repositories;

internal sealed class TenantsRepository(TenantsDbContext db, INpgsqlConnectionFactory? conn = null) : ITenantsRepository
{
    private INpgsqlConnectionFactory Conn =>
        conn ?? throw new System.InvalidOperationException("TenantsRepository reads require a database connection (Dapper-only; no EF fallback).");

    public async Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """SELECT "Id", "Name", "CreatedAtUtc" FROM tenants.tenants ORDER BY "Id" """;
        await using var c = Conn.Create();
        await c.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<TenantRecord>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<TenantRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        const string sql = """SELECT "Id", "Name", "CreatedAtUtc" FROM tenants.tenants WHERE "Id" = @id""";
        await using var c = Conn.Create();
        await c.OpenAsync(ct).ConfigureAwait(false);
        return await c.QueryFirstOrDefaultAsync<TenantRecord>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task AddAsync(TenantRecord tenant, CancellationToken ct = default)
    {
        System.ArgumentNullException.ThrowIfNull(tenant);
        db.Tenants.Add(new TenantEntity
        {
            Id = tenant.Id,
            Name = tenant.Name,
            CreatedAtUtc = tenant.CreatedAtUtc,
        });
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class NullTenantsRepository : ITenantsRepository
{
    public Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TenantRecord>>([]);

    public Task<TenantRecord?> GetAsync(string id, CancellationToken ct = default) =>
        Task.FromResult<TenantRecord?>(null);

    public Task AddAsync(TenantRecord tenant, CancellationToken ct = default) => Task.CompletedTask;
}
