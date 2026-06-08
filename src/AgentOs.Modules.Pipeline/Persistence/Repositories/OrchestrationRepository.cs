// Orchestration CRUD. Writes stamp TenantId via EF; reads run as Dapper SQL when a raw connection is
// wired (INpgsqlConnectionFactory), else EF fallback. TENANT SAFETY: the EF global query filter does
// not apply on the Dapper path, so every Dapper read carries an explicit `WHERE "TenantId" = @tenantId`
// (resolved from ITenantContext). Columns are PascalCase (quoted); table is pipeline.orchestrations.
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Persistence;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Persistence.Entities;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Pipeline.Persistence.Repositories;

internal sealed class OrchestrationRepository(PipelineDbContext db, ITenantContext tenant, INpgsqlConnectionFactory? conn = null) : IOrchestrationRepository
{
    public async Task<IReadOnlyList<OrchestrationRecord>> ListAsync(CancellationToken ct = default)
    {
        if (conn is null)
        {
            return await db.Orchestrations
                .AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new OrchestrationRecord(x.Id, x.Name, x.Description, x.DefinitionJson, x.UpdatedAtUtc))
                .ToListAsync(ct);
        }

        const string sql = """
            SELECT "Id", "Name", "Description", "DefinitionJson", "UpdatedAtUtc"
            FROM pipeline.orchestrations
            WHERE "TenantId" = @tenantId
            ORDER BY "Name"
            """;
        await using var c = conn.Create();
        await c.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<OrchestrationRecord>(
            new CommandDefinition(sql, new { tenantId = tenant.TenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<OrchestrationRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        if (conn is null)
        {
            var e = await db.Orchestrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return e is null ? null : new OrchestrationRecord(e.Id, e.Name, e.Description, e.DefinitionJson, e.UpdatedAtUtc);
        }

        const string sql = """
            SELECT "Id", "Name", "Description", "DefinitionJson", "UpdatedAtUtc"
            FROM pipeline.orchestrations
            WHERE "Id" = @id AND "TenantId" = @tenantId
            """;
        await using var c = conn.Create();
        await c.OpenAsync(ct).ConfigureAwait(false);
        return await c.QueryFirstOrDefaultAsync<OrchestrationRecord>(
            new CommandDefinition(sql, new { id, tenantId = tenant.TenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task UpsertAsync(OrchestrationRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var existing = await db.Orchestrations.FirstOrDefaultAsync(x => x.Id == record.Id, ct);
        if (existing is null)
        {
            db.Orchestrations.Add(new OrchestrationEntity
            {
                Id = record.Id,
                TenantId = tenant.TenantId,
                Name = record.Name,
                Description = record.Description,
                DefinitionJson = record.DefinitionJson,
                UpdatedAtUtc = record.UpdatedAtUtc,
            });
        }
        else
        {
            existing.Name = record.Name;
            existing.Description = record.Description;
            existing.DefinitionJson = record.DefinitionJson;
            existing.UpdatedAtUtc = record.UpdatedAtUtc;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var e = await db.Orchestrations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is not null)
        {
            db.Orchestrations.Remove(e);
            await db.SaveChangesAsync(ct);
        }
    }
}
