// Decorator around IOrchestratorAgent: generates a RunId, sets the MetricsContext (so each per-call
// RunMetric carries the RunId), runs the pipeline, then saves the PipelineResult + metrics to the DB.
// Persistence is best-effort — a DB error must not corrupt the result of a successful run.
using AgenticSdlc.Modules.Pipeline.Agents;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Modules.Pipeline.Persistence;
using AgenticSdlc.Domain.Pipeline;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Modules.Pipeline.Orchestration;

internal sealed class PersistingOrchestratorAgent : IOrchestratorAgent
{
    private readonly IOrchestratorAgent _inner;
    private readonly IPipelineRunRepository _repository;
    private readonly IMetricsCollector _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<PersistingOrchestratorAgent> _logger;

    public PersistingOrchestratorAgent(
        IOrchestratorAgent inner,
        IPipelineRunRepository repository,
        IMetricsCollector metrics,
        TimeProvider clock,
        ILogger<PersistingOrchestratorAgent> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PipelineResult> RunAsync(UserStory story, CancellationToken cancellationToken = default)
    {
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
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Persist best-effort: a DB error must not corrupt a successful run.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Failed to save pipeline run {RunId} — the result is still returned to the client.", runId);
        }
    }
}
