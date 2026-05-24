// AgenticSdlc.Domain/AgentMetrics.cs
// Phase 3 — Common metrics attached to every agent result (cost, token, latency).

namespace AgenticSdlc.Domain;

/// <summary>
/// Common metric embedded in every agent result — used for KC1-KC5 benchmarks and cost reporting.
/// </summary>
/// <param name="Provider">Name of the provider that was called (Claude / AzureOpenAI / Mock).</param>
/// <param name="Model">Model alias that was used.</param>
/// <param name="InputTokens">Input tokens consumed.</param>
/// <param name="OutputTokens">Output tokens generated.</param>
/// <param name="CostUsd">Estimated cost in USD (CostCalculator).</param>
/// <param name="Latency">End-to-end agent time (including LLM call + parse + retry).</param>
public sealed record AgentMetrics(
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    System.TimeSpan Latency)
{
    /// <summary>Empty metrics — used for test scenarios or placeholder initialization.</summary>
    public static AgentMetrics Empty { get; } =
        new(Provider: "None", Model: "None", InputTokens: 0, OutputTokens: 0, CostUsd: 0m, Latency: System.TimeSpan.Zero);

    /// <summary>Sum of two metrics (accumulated for the pipeline result aggregate).</summary>
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
