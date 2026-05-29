// Repository for pipeline run + artifact + metrics. Interface in Application (Clean Arch),
// EF Core impl in Infrastructure. Domain stays pure and knows nothing about the DB.
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Domain.Pipeline;

namespace AgenticSdlc.Modules.Pipeline.Persistence;

/// <summary>Stores + queries the pipeline run history.</summary>
public interface IPipelineRunRepository
{
    /// <summary>Stores a single run (full PipelineResult + list of RunMetric per LLM call).</summary>
    Task SaveAsync(PipelineRunRecord record, CancellationToken ct = default);

    /// <summary>Gets a single full run by Id (null if not found).</summary>
    Task<PipelineRunRecord?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>List of the most recent runs (summary, without the artifact json).</summary>
    Task<IReadOnlyList<PipelineRunSummary>> ListAsync(int limit = 50, CancellationToken ct = default);
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
