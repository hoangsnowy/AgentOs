// AgenticSdlc.Application/Metrics/RunMetric.cs
// Sprint 4 — 1 record cho 1 LLM call. Sink ra CSV cho Bảng 2.6 luận văn.

using System;

namespace AgenticSdlc.Application.Metrics;

/// <summary>1 record cho 1 LLM call trong 1 iteration của 1 KC run.</summary>
/// <param name="RunId">UUID của 1 pipeline run (cùng RunId → cùng 1 lần chạy pipeline).</param>
/// <param name="KcId">Mã KC: KC1..KC5, hoặc "ad-hoc" cho call ngoài KC bench.</param>
/// <param name="Iteration">Số iteration của QA loop (1..NMax), 0 nếu là requirement (chạy 1 lần).</param>
/// <param name="AgentName">RequirementAgent / CodingAgent / TestingAgent / QaAgent.</param>
/// <param name="Model">Model dùng (vd "claude-sonnet-4-5", "gpt-4.1").</param>
/// <param name="Provider">Provider (Claude / AzureOpenAI / Mock).</param>
/// <param name="TokensIn">Input tokens.</param>
/// <param name="TokensOut">Output tokens.</param>
/// <param name="LatencyMs">Tổng wall time của LLM call (ms).</param>
/// <param name="CostUsd">Cost ước tính (USD).</param>
/// <param name="Success">True nếu call success (parse + validate OK), false nếu fail.</param>
/// <param name="ErrorMessage">Lỗi (nếu fail).</param>
/// <param name="Timestamp">UTC timestamp khi call kết thúc.</param>
public sealed record RunMetric(
    string RunId,
    string KcId,
    int Iteration,
    string AgentName,
    string Model,
    string Provider,
    int TokensIn,
    int TokensOut,
    double LatencyMs,
    decimal CostUsd,
    bool Success,
    string? ErrorMessage,
    DateTimeOffset Timestamp);
