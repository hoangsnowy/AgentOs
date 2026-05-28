// Persistence entity for a single pipeline run. Relational columns for querying (analytics) +
// the jsonb ResultJson column holding the full serialized PipelineResult.
namespace AgenticSdlc.Infrastructure.Persistence.Entities;

internal sealed class PipelineRunEntity
{
    public Guid Id { get; set; }

    public string UserStoryText { get; set; } = string.Empty;

    /// <summary>PipelineStatus enum name: Done / MaxIterationReached / Failed.</summary>
    public string Status { get; set; } = string.Empty;

    public decimal TotalCostUsd { get; set; }

    public int TotalTokensIn { get; set; }

    public int TotalTokensOut { get; set; }

    public int IterationCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Full PipelineResult serialized as JSON (jsonb column).</summary>
    public string ResultJson { get; set; } = string.Empty;

    public List<RunMetricEntity> Metrics { get; } = [];
}
