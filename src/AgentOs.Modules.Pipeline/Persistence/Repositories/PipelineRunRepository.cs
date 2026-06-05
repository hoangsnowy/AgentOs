// EF Core impl: saves PipelineResult (jsonb) + RunMetric rows, reads back + lists summaries.
// Writes stamp TenantId from ITenantContext; reads are filtered by the DbContext's global query
// filter so a request only ever sees its own tenant's runs.
using System.Globalization;
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

        // Stream the (tenant-, since-) filtered rows once and fold them into small per-key
        // accumulators, instead of materializing every row into a List and then grouping it four ways
        // in memory. Memory is O(distinct keys), not O(rows); the (TenantId, TimestampUtc) index serves
        // the filter. We project to the cost columns only (not the row entity / its JSON blob), and
        // bucket by agent / provider / model / day in a single pass. (Pushing the GROUP BY fully into
        // SQL would also cut transfer, but EF's GROUP BY translation isn't portable to the in-memory
        // test provider — this keeps the logic provider-agnostic.)
        var byAgent = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var byProvider = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var byModel = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var byDay = new Dictionary<string, Acc>(StringComparer.Ordinal);
        var runIds = new HashSet<Guid>();
        var total = default(Acc);

        var stream = q
            .Select(m => new
            {
                m.RunId,
                m.AgentName,
                m.Provider,
                m.Model,
                m.TokensIn,
                m.TokensOut,
                m.CostUsd,
                m.TimestampUtc,
            })
            .AsAsyncEnumerable();

        await foreach (var m in stream.WithCancellation(ct).ConfigureAwait(false))
        {
            total.Add(m.CostUsd, m.TokensIn, m.TokensOut);
            runIds.Add(m.RunId);
            Bump(byAgent, m.AgentName, m.CostUsd, m.TokensIn, m.TokensOut);
            Bump(byProvider, m.Provider, m.CostUsd, m.TokensIn, m.TokensOut);
            Bump(byModel, m.Model, m.CostUsd, m.TokensIn, m.TokensOut);
            var day = m.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Bump(byDay, day, m.CostUsd, m.TokensIn, m.TokensOut);
        }

        if (total.Calls == 0)
        {
            return CostSummary.Empty;
        }

        return new CostSummary(
            total.CostUsd, total.TokensIn, total.TokensOut, total.Calls, runIds.Count,
            ByCostDesc(byAgent), ByCostDesc(byProvider), ByCostDesc(byModel),
            [.. byDay.Select(kv => kv.Value.ToBucket(kv.Key)).OrderBy(b => b.Key, StringComparer.Ordinal)]);
    }

    private static void Bump(Dictionary<string, Acc> map, string key, decimal cost, int tokensIn, int tokensOut)
    {
        // CollectionsMarshal would avoid the double lookup, but a plain get/set keeps it simple; the
        // map is keyed by a bounded set (agent / provider / model names, or days).
        map.TryGetValue(key, out var acc);
        acc.Add(cost, tokensIn, tokensOut);
        map[key] = acc;
    }

    private static List<CostBucket> ByCostDesc(Dictionary<string, Acc> map) =>
        [.. map.Select(kv => kv.Value.ToBucket(kv.Key)).OrderByDescending(b => b.CostUsd)];

    // Mutable fold accumulator for one bucket (or the grand total).
    private struct Acc
    {
        public decimal CostUsd;
        public int TokensIn;
        public int TokensOut;
        public int Calls;

        public void Add(decimal cost, int tokensIn, int tokensOut)
        {
            CostUsd += cost;
            TokensIn += tokensIn;
            TokensOut += tokensOut;
            Calls++;
        }

        public readonly CostBucket ToBucket(string key) => new(key, CostUsd, TokensIn, TokensOut, Calls);
    }
}
