// EF Core impl: saves PipelineResult (jsonb) + RunMetric rows, reads back + lists summaries.
// Writes stamp TenantId from ITenantContext; reads are filtered by the DbContext's global query
// filter so a request only ever sees its own tenant's runs.
using System.Text.Json;
using AgentOs.SharedKernel.Identity;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Domain.Pipeline;
using AgentOs.Modules.Pipeline.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentOs.Modules.Pipeline.Persistence.Repositories;

internal sealed class PipelineRunRepository(PipelineDbContext db, ITenantContext tenant) : IPipelineRunRepository
{
    public async Task SaveAsync(PipelineRunRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var result = record.Result;
        var tenantId = tenant.TenantId;

        var entity = new PipelineRunEntity
        {
            Id = record.Id,
            TenantId = tenantId,
            UserStoryText = result.UserStory.Description,
            Status = result.Status.ToString(),
            TotalCostUsd = result.TotalMetrics.CostUsd,
            TotalTokensIn = result.TotalMetrics.InputTokens,
            TotalTokensOut = result.TotalMetrics.OutputTokens,
            IterationCount = result.IterationCount,
            CreatedAtUtc = record.CreatedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc,
            ResultJson = JsonSerializer.Serialize(result, PersistenceJson.Options),
        };

        foreach (var m in record.Metrics)
        {
            entity.Metrics.Add(new RunMetricEntity
            {
                RunId = record.Id,
                TenantId = tenantId,
                KcId = m.KcId,
                Iteration = m.Iteration,
                AgentName = m.AgentName,
                Model = m.Model,
                Provider = m.Provider,
                TokensIn = m.TokensIn,
                TokensOut = m.TokensOut,
                LatencyMs = m.LatencyMs,
                CostUsd = m.CostUsd,
                Success = m.Success,
                ErrorMessage = m.ErrorMessage,
                TimestampUtc = m.Timestamp,
            });
        }

        db.PipelineRuns.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PipelineRunRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.PipelineRuns
            .Include(x => x.Metrics)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<PipelineResult>(entity.ResultJson, PersistenceJson.Options);
        if (result is null)
        {
            return null;
        }

        var metrics = entity.Metrics
            .OrderBy(m => m.Id)
            .Select(m => new RunMetric(
                entity.Id.ToString(),
                m.KcId,
                m.Iteration,
                m.AgentName,
                m.Model,
                m.Provider,
                m.TokensIn,
                m.TokensOut,
                m.LatencyMs,
                m.CostUsd,
                m.Success,
                m.ErrorMessage,
                m.TimestampUtc))
            .ToList();

        return new PipelineRunRecord(
            entity.Id,
            result,
            metrics,
            entity.CreatedAtUtc,
            entity.CompletedAtUtc ?? entity.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<PipelineRunSummary>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        return await db.PipelineRuns
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new PipelineRunSummary(
                x.Id,
                x.Status,
                x.TotalCostUsd,
                x.IterationCount,
                x.CreatedAtUtc,
                x.CompletedAtUtc,
                x.UserStoryText.Length > 120 ? x.UserStoryText.Substring(0, 120) : x.UserStoryText))
            .ToListAsync(ct);
    }

    public async Task<CostSummary> GetCostSummaryForTenantAsync(
        string tenantId, DateTimeOffset? since = null, CancellationToken ct = default)
    {
        // Tenant-explicit: bypass the ITenantContext-driven global query filter (a Blazor circuit has
        // no HttpContext, so ITenantContext is blank) and scope to the tenant the caller passed in.
        var q = db.RunMetrics
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId);
        if (since is { } cutoff)
        {
            q = q.Where(m => m.TimestampUtc >= cutoff);
        }

        var rows = await q
            .Select(m => new CostRow(
                m.RunId, m.AgentName, m.Provider, m.Model,
                m.TokensIn, m.TokensOut, m.CostUsd, m.TimestampUtc))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return CostSummary.Empty;
        }

        List<CostBucket> By(Func<CostRow, string> key) =>
            [.. rows
                .GroupBy(key)
                .Select(g => new CostBucket(
                    g.Key,
                    g.Sum(r => r.CostUsd),
                    g.Sum(r => r.TokensIn),
                    g.Sum(r => r.TokensOut),
                    g.Count()))
                .OrderByDescending(b => b.CostUsd)];

        var byDay = rows
            .GroupBy(r => r.TimestampUtc.UtcDateTime.Date)
            .Select(g => new CostBucket(
                g.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                g.Sum(r => r.CostUsd),
                g.Sum(r => r.TokensIn),
                g.Sum(r => r.TokensOut),
                g.Count()))
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();

        return new CostSummary(
            rows.Sum(r => r.CostUsd),
            rows.Sum(r => r.TokensIn),
            rows.Sum(r => r.TokensOut),
            rows.Count,
            rows.Select(r => r.RunId).Distinct().Count(),
            By(r => r.Agent),
            By(r => r.Provider),
            By(r => r.Model),
            byDay);
    }

    // Flattened metric row for in-memory grouping (one materialize, then group 4 ways).
    private readonly record struct CostRow(
        Guid RunId, string Agent, string Provider, string Model,
        int TokensIn, int TokensOut, decimal CostUsd, DateTimeOffset TimestampUtc);
}
