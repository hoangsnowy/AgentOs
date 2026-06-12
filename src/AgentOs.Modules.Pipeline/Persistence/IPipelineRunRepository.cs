// Repository for pipeline run + artifact + metrics. Interface in Application (Clean Arch),
// EF Core impl in Infrastructure. Domain stays pure and knows nothing about the DB.
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Domain.Pipeline;

namespace AgentOs.Modules.Pipeline.Persistence;

/// <summary>Stores + queries the pipeline run history.</summary>
public interface IPipelineRunRepository
{
    /// <summary>Stores a single run (full PipelineResult + list of RunMetric per LLM call).</summary>
    Task SaveAsync(PipelineRunRecord record, CancellationToken ct = default);

    /// <summary>Gets a single full run by Id (null if not found).</summary>
    Task<PipelineRunRecord?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>List of the most recent runs (summary, without the artifact json), newest first.
    /// <paramref name="offset"/> skips that many rows for paging.</summary>
    Task<IReadOnlyList<PipelineRunSummary>> ListAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>Aggregated LLM cost for one tenant since an optional cutoff (null = all time).
    /// Tenant-explicit: the caller passes the tenant id so this is safe to call from a Blazor
    /// circuit, where ITenantContext is blank (no HttpContext) and the DbContext's global query
    /// filter cannot resolve the tenant on its own.</summary>
    Task<CostSummary> GetCostSummaryForTenantAsync(
        string tenantId, DateTimeOffset? since = null, CancellationToken ct = default);
}

/// <summary>A single full run to store / read back.</summary>
public sealed record PipelineRunRecord(
    Guid Id,
    PipelineResult Result,
    IReadOnlyList<RunMetric> Metrics,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

/// <summary>Summary of a single run for the history list.</summary>
public sealed record PipelineRunSummary(
    Guid Id,
    string Status,
    decimal TotalCostUsd,
    int IterationCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string UserStoryPreview);

/// <summary>Aggregated LLM spend for a tenant — headline totals plus breakdowns for the cost dashboard.</summary>
public sealed record CostSummary(
    decimal TotalCostUsd,
    int TotalTokensIn,
    int TotalTokensOut,
    int CallCount,
    int RunCount,
    IReadOnlyList<CostBucket> ByAgent,
    IReadOnlyList<CostBucket> ByProvider,
    IReadOnlyList<CostBucket> ByModel,
    IReadOnlyList<CostBucket> ByDay)
{
    /// <summary>The zero summary — no metrics recorded (or no database wired).</summary>
    public static CostSummary Empty { get; } = new(0m, 0, 0, 0, 0, [], [], [], []);
}

/// <summary>One row of a cost breakdown grouped by agent, provider, model or day.</summary>
public sealed record CostBucket(
    string Key,
    decimal CostUsd,
    int TokensIn,
    int TokensOut,
    int Calls);
