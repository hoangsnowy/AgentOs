// AgenticSdlc.Domain/AgentMetrics.cs
// Phase 3 — Common metrics đính kèm mọi kết quả tác tử (cost, token, latency).

namespace AgenticSdlc.Domain;

/// <summary>
/// Metric chung mọi agent result embed — phục vụ benchmark KC1-KC5 và cost-report.
/// </summary>
/// <param name="Provider">Tên provider đã gọi (Claude / AzureOpenAI / Mock).</param>
/// <param name="Model">Model alias đã dùng.</param>
/// <param name="InputTokens">Token input đã tiêu thụ.</param>
/// <param name="OutputTokens">Token output đã sinh.</param>
/// <param name="CostUsd">Cost ước tính USD (CostCalculator).</param>
/// <param name="Latency">Thời gian end-to-end của agent (gồm cả LLM call + parse + retry).</param>
public sealed record AgentMetrics(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    System.TimeSpan Latency)
{
    /// <summary>Empty metrics — dùng cho trường hợp test hoặc khởi tạo placeholder.</summary>
    public static AgentMetrics Empty { get; } =
        new(Provider: "None", Model: "None", InputTokens: 0, OutputTokens: 0, CostUsd: 0m, Latency: System.TimeSpan.Zero);

    /// <summary>Tổng 2 metric (cộng dồn cho pipeline result aggregate).</summary>
    public AgentMetrics Add(AgentMetrics other)
    {
        System.ArgumentNullException.ThrowIfNull(other);
        return new AgentMetrics(
            Provider: $"{Provider}+{other.Provider}",
            Model: $"{Model}+{other.Model}",
            InputTokens: InputTokens + other.InputTokens,
            OutputTokens: OutputTokens + other.OutputTokens,
            CostUsd: CostUsd + other.CostUsd,
            Latency: Latency + other.Latency);
    }
}
