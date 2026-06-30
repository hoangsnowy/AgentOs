// Repository for pipeline run + artifact + metrics. Interface in Application (Clean Arch),
// EF Core impl in Infrastructure. Domain stays pure and knows nothing about the DB.
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Domain.Pipeline;

namespace AgentOs.Modules.Pipeline.Persistence;

/// <summary>Stores + queries the pipeline run history.</summary>
public interface IPipelineRunRepository
{
    /// <summary>Stores a single run (full PipelineResult + list of RunMetric per LLM call). Stamps the
    /// owning tenant from the ambient <c>ITenantContext</c>.</summary>
    Task SaveAsync(PipelineRunRecord record, CancellationToken ct = default);

    /// <summary>Stores a single run stamping an <b>explicit</b> tenant id rather than reading it from the
    /// ambient <c>ITenantContext</c>. Required for callers with no request scope — a Blazor circuit running
    /// the Workflow studio has a blank <c>ITenantContext</c>, so the run + its <c>run_metrics</c> rows must
    /// carry the signed-in tenant passed in, or the budget gate would never see (and so never cap) that
    /// tenant's workflow spend.</summary>
    Task SaveAsync(PipelineRunRecord record, string tenantId, CancellationToken ct = default);

    /// <summary>Gets a single full run by Id (null if not found).</summary>
    Task<PipelineRunRecord?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>List of the most recent runs (summary, without the artifact json), newest first.
    /// <paramref name="offset"/> skips that many rows for paging.</summary>
    Task<IReadOnlyList<PipelineRunSummary>> ListAsync(int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>Most recent runs for one tenant, newest first. <b>Tenant-explicit</b>: the caller passes
    /// the tenant id rather than relying on the DbContext's <c>ITenantContext</c>-driven global query
    /// filter, which is blank in a Blazor circuit (no HttpContext). Use this from the desktop (Overview /
    /// run history) so the list is scoped to the signed-in tenant instead of leaking or coming back empty.</summary>
    Task<IReadOnlyList<PipelineRunSummary>> ListForTenantAsync(
        string tenantId, int limit = 50, int offset = 0, CancellationToken ct = default);

    /// <summary>Aggregated LLM cost for one tenant since an optional cutoff (null = all time).
    /// Tenant-explicit: the caller passes the tenant id so this is safe to call from a Blazor
    /// circuit, where ITenantContext is blank (no HttpContext) and the DbContext's global query
    /// filter cannot resolve the tenant on its own.</summary>
    Task<CostSummary> GetCostSummaryForTenantAsync(
        string tenantId, DateTimeOffset? since = null, CancellationToken ct = default);

    /// <summary>Just the total LLM spend (USD) for one tenant since an optional cutoff — a single
    /// <c>SUM(CostUsd)</c>, no breakdown buckets. The budget gate (<c>BudgetGuard</c>) fires this per run and
    /// only needs the headline number, so it skips the four extra GROUP BY round-trips that
    /// <see cref="GetCostSummaryForTenantAsync"/> does for the cost dashboard. Tenant-explicit (safe from a
    /// blank-ITenantContext Blazor circuit).</summary>
    Task<decimal> GetSpendForTenantAsync(
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
