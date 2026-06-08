// Decorator around IOrchestratorAgent: generates a RunId, sets the MetricsContext (so each per-call
// RunMetric carries the RunId), runs the pipeline, then saves the PipelineResult + metrics to the DB.
// Persistence is best-effort — a DB error must not corrupt the result of a successful run.
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Domain.Cost;
using AgentOs.Domain.Pipeline;
using AgentOs.SharedKernel.Identity;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Pipeline.Orchestration;

internal sealed class PersistingOrchestratorAgent : IOrchestratorAgent
{
    private readonly IOrchestratorAgent _inner;
    private readonly IPipelineRunRepository _repository;
    private readonly IMetricsCollector _metrics;
    private readonly IBudgetGuard _budgetGuard;
    private readonly TimeProvider _clock;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PersistingOrchestratorAgent> _logger;

    public PersistingOrchestratorAgent(
        IOrchestratorAgent inner,
        IPipelineRunRepository repository,
        IMetricsCollector metrics,
        IBudgetGuard budgetGuard,
        TimeProvider clock,
        ITenantContext tenantContext,
        ILogger<PersistingOrchestratorAgent> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _budgetGuard = budgetGuard ?? throw new ArgumentNullException(nameof(budgetGuard));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PipelineResult> RunAsync(UserStory story, CancellationToken cancellationToken = default)
    {
        // Run-level budget gate: protect the whole (expensive) pipeline run with one spend check.
        // Ambient identity (background Task.Run) wins; the request-scoped ITenantContext is the fallback
        // — never a hardcoded `default`, which would bill a low-budget tenant's run against `default`.
        var tenantId = AmbientIdentity.Current?.TenantId is { Length: > 0 } ambient
            ? ambient
            : _tenantContext.TenantId;
        var budget = await _budgetGuard.EvaluateAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (budget is { State: BudgetState.Exceeded, EnforceOn: true })
        {
            throw new BudgetExceededException(tenantId, budget.CapUsd, budget.SpentUsd);
        }
        if (budget.State == BudgetState.Warn)
        {
            _logger.LogWarning(
                "LLM budget warning for tenant {Tenant}: spent ${Spent} of ${Cap} ({Percent:P0}) this month.",
                tenantId, budget.SpentUsd, budget.CapUsd, budget.Percent);
        }

        var runId = Guid.NewGuid();
        var createdAtUtc = _clock.GetUtcNow();

        PipelineResult result;
        using (MetricsContext.BeginScope(runId.ToString(), "ad-hoc"))
        {
            result = await _inner.RunAsync(story, cancellationToken).ConfigureAwait(false);
        }

        var completedAtUtc = _clock.GetUtcNow();
        await PersistAsync(runId, result, createdAtUtc, completedAtUtc, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task PersistAsync(
        Guid runId,
        PipelineResult result,
        DateTimeOffset createdAtUtc,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var runKey = runId.ToString();
            var runMetrics = _metrics.Snapshot()
                .Where(m => string.Equals(m.RunId, runKey, StringComparison.Ordinal))
                .ToList();

            await _repository.SaveAsync(
                new PipelineRunRecord(runId, result, runMetrics, createdAtUtc, completedAtUtc),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            OnPersistFailed(runId, ex);
        }
        catch (System.Data.Common.DbException ex)
        {
            OnPersistFailed(runId, ex);
        }
        catch (TimeoutException ex)
        {
            OnPersistFailed(runId, ex);
        }
        catch (IOException ex)
        {
            OnPersistFailed(runId, ex);
        }
        catch (InvalidOperationException ex)
        {
            OnPersistFailed(runId, ex);
        }

        // Persist best-effort: a DB error must not corrupt a successful run — log and move on.
        void OnPersistFailed(Guid id, Exception ex) =>
            _logger.LogError(ex, "Failed to save pipeline run {RunId} — the result is still returned to the client.", id);
    }
}
