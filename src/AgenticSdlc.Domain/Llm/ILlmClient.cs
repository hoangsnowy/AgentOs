// AgenticSdlc.Domain/Llm/ILlmClient.cs
// Sprint 1 — Contract for the LLM Gateway (Domain layer, does not reference Infrastructure).

namespace AgenticSdlc.Domain.Llm;

/// <summary>
/// Abstraction for every LLM provider (Claude, Azure OpenAI, Mock, ...).
/// The 5 agents (Requirement / Coding / Testing / QA / Orchestrator) depend on this interface — NOT on a
/// concrete client — following the Dependency Inversion Principle (thesis Section 2.4.2).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Provider tag, used for logging and <see cref="LlmResponse.Provider"/>.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Sends a single request to the provider and returns the response. All retry/timeout logic must be handled internally.
    /// Throws <see cref="LlmException"/> when retries are exhausted or a non-recoverable error occurs.
    /// </summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response with content, token usage, cost, latency.</returns>
    System.Threading.Tasks.Task<LlmResponse> SendAsync(
        LlmRequest request,
        System.Threading.CancellationToken cancellationToken = default);
}
