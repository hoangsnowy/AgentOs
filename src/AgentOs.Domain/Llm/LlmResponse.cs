// AgentOs.Domain/Llm/LlmResponse.cs
// Sprint 1 — LLM Gateway contract (response side).

namespace AgentOs.Domain.Llm;

/// <summary>
/// Response returned from <see cref="ILlmClient.SendAsync(LlmRequest, System.Threading.CancellationToken)"/>.
/// </summary>
/// <param name="Content">Text content (extracted from the provider's shape). Required, may be empty.</param>
/// <param name="InputTokens">Number of input tokens consumed.</param>
/// <param name="OutputTokens">Number of output tokens generated.</param>
/// <param name="CostUsd">Estimated cost in USD (computed by <c>CostCalculator</c>).</param>
/// <param name="Latency">End-to-end time (measured with <see cref="System.Diagnostics.Stopwatch"/> in the client).</param>
/// <param name="Model">Name of the model that actually generated the response (the provider may alias it).</param>
/// <param name="Provider">Provider name: <c>"Claude"</c>, <c>"AzureOpenAI"</c>, or <c>"MAF"</c>.</param>
public sealed record LlmResponse(
    string Content,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    System.TimeSpan Latency,
    string Model,
    string Provider);
