// AgenticSdlc.Application/Metrics/RunMetricFactory.cs
// Sprint 4 — build RunMetric từ LlmResponse + ambient MetricsContext.

using System;
using AgenticSdlc.Domain.Llm;

namespace AgenticSdlc.Application.Metrics;

/// <summary>Helper build <see cref="RunMetric"/> từ <see cref="LlmResponse"/> + ambient <see cref="MetricsContext"/>.</summary>
public static class RunMetricFactory
{
    /// <summary>Build record (dùng <see cref="MetricsContext.Current"/> nếu set, fallback "ad-hoc").</summary>
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
