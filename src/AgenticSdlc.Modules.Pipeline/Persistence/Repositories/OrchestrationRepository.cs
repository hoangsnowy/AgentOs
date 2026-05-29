// EF Core impl for Agent Studio orchestration CRUD. Writes stamp TenantId; reads filtered by the
// DbContext global query filter so each tenant only sees its own graphs.
using AgenticSdlc.SharedKernel.Identity;
using AgenticSdlc.Modules.Pipeline.Persistence;
using AgenticSdlc.Modules.Pipeline.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgenticSdlc.Modules.Pipeline.Persistence.Repositories;

internal sealed class OrchestrationRepository(PipelineDbContext db, ITenantContext tenant) : IOrchestrationRepository
{
    public async Task<IReadOnlyList<OrchestrationRecord>> ListAsync(CancellationToken ct = default)
    {
        return await db.Orchestrations
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new OrchestrationRecord(x.Id, x.Name, x.Description, x.DefinitionJson, x.UpdatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<OrchestrationRecord?> GetAsync(string id, CancellationToken ct = default)
    {
        var e = await db.Orchestrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? null : new OrchestrationRecord(e.Id, e.Name, e.Description, e.DefinitionJson, e.UpdatedAtUtc);
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
