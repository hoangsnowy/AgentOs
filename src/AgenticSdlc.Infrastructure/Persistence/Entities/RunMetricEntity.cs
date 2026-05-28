// Persistence entity for a single LLM call (maps 1-1 from Application.Metrics.RunMetric).
// Relational → run_metrics table, SQL queries for cost-report / kc-bench.
namespace AgenticSdlc.Infrastructure.Persistence.Entities;

internal sealed class RunMetricEntity
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public string KcId { get; set; } = string.Empty;

    public int Iteration { get; set; }

    public string AgentName { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public int TokensIn { get; set; }

    public int TokensOut { get; set; }

    public double LatencyMs { get; set; }

    public decimal CostUsd { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }
}
