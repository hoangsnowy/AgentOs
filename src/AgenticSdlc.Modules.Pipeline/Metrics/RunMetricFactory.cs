// AgenticSdlc.Application/Metrics/RunMetricFactory.cs
// Sprint 4 — builds RunMetric from LlmResponse + ambient MetricsContext.

using System;
using AgenticSdlc.Domain.Llm;

namespace AgenticSdlc.Modules.Pipeline.Metrics;

/// <summary>Helper that builds a <see cref="RunMetric"/> from an <see cref="LlmResponse"/> + ambient <see cref="MetricsContext"/>.</summary>
public static class RunMetricFactory
{
    /// <summary>Builds the record (uses <see cref="MetricsContext.Current"/> if set, falling back to "ad-hoc").</summary>
    public static RunMetric From(LlmResponse response, string agentName, bool success, string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var ctx = MetricsContext.Current;
        return new RunMetric(
            RunId: ctx?.RunId ?? Guid.NewGuid().ToString("N"),
            KcId: ctx?.KcId ?? "ad-hoc",
            Iteration: ctx?.Iteration ?? 0,
            AgentName: agentName,
            Model: response.Model,
            Provider: response.Provider,
            TokensIn: response.InputTokens,
            TokensOut: response.OutputTokens,
            LatencyMs: response.Latency.TotalMilliseconds,
            CostUsd: response.CostUsd,
            Success: success,
            ErrorMessage: errorMessage,
            Timestamp: DateTimeOffset.UtcNow);
    }
}
