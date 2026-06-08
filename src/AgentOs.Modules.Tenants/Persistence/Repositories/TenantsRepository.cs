// Tenant registry. Spans every tenant — this table IS the tenant list, so (unlike tenant-scoped
// repos) its reads carry NO tenant filter; admin-policy guards live in the API endpoints. Reads run as
// Dapper SQL when a raw connection is wired (INpgsqlConnectionFactory), else EF fallback; EF owns writes.
// Columns are PascalCase (quoted); table is tenants.tenants.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Tenants.Persistence.Entities;
using AgentOs.SharedKernel.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Tenants.Persistence.Repositories;

internal sealed class TenantsRepository(TenantsDbContext db, INpgsqlConnectionFactory? conn = null) : ITenantsRepository
{
    public async Task<IReadOnlyList<TenantRecord>> ListAsync(CancellationToken ct = default)
    {
        if (conn is null)
        {
            return await db.Tenants.AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new TenantRecord(x.Id, x.Name, x.CreatedAtUtc))
                .ToListAsync(ct);
        }

        const string sql = """SELECT "Id", "Name", "CreatedAtUtc" FROM tenants.tenants ORDER BY "Id" """;
        await using var c = conn.Create();
        await c.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<TenantRecord>(new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<TenantRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        if (conn is null)
        {
            var e = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return e is null ? null : new TenantRecord(e.Id, e.Name, e.CreatedAtUtc);
        }

        const string sql = """SELECT "Id", "Name", "CreatedAtUtc" FROM tenants.tenants WHERE "Id" = @id""";
        await using var c = conn.Create();
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
