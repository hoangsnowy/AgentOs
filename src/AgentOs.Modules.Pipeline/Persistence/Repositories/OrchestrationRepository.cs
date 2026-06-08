// Orchestration CRUD. Writes stamp TenantId via EF; reads run as Dapper SQL over a raw connection
// (Dapper-only — no EF read fallback). The real repo is registered only with a connection string (else
// the no-op repo), so the factory is always present in production; a null factory throws. TENANT SAFETY:
// the EF global query filter does not apply on the Dapper path, so every read carries an explicit
// `WHERE "TenantId" = @tenantId` (resolved from ITenantContext). Columns PascalCase (quoted); table pipeline.orchestrations.
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Persistence;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Persistence.Entities;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Pipeline.Persistence.Repositories;

internal sealed class OrchestrationRepository(PipelineDbContext db, ITenantContext tenant, INpgsqlConnectionFactory? conn = null) : IOrchestrationRepository
{
    private INpgsqlConnectionFactory Conn =>
        conn ?? throw new System.InvalidOperationException("OrchestrationRepository reads require a database connection (Dapper-only; no EF fallback).");

    public async Task<IReadOnlyList<OrchestrationRecord>> ListAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT "Id", "Name", "Description", "DefinitionJson", "UpdatedAtUtc"
            FROM pipeline.orchestrations
            WHERE "TenantId" = @tenantId
            ORDER BY "Name"
            """;
        await using var c = Conn.Create();
        await c.OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<OrchestrationRecord>(
            new CommandDefinition(sql, new { tenantId = tenant.TenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<OrchestrationRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT "Id", "Name", "Description", "DefinitionJson", "UpdatedAtUtc"
            FROM pipeline.orchestrations
            WHERE "Id" = @id AND "TenantId" = @tenantId
            """;
        await using var c = Conn.Create();
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
