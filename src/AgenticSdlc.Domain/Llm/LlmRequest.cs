// AgenticSdlc.Domain/Llm/LlmRequest.cs
// Sprint 1 — LLM Gateway contract (request side).

namespace AgenticSdlc.Domain.Llm;

/// <summary>
/// Request passed into <see cref="ILlmClient.SendAsync(LlmRequest, System.Threading.CancellationToken)"/>.
/// This is a provider-neutral DTO — <see cref="ILlmClient"/> maps it to the Anthropic / Azure OpenAI shape.
/// </summary>
/// <param name="SystemPrompt">System / role prompt applied to the agent. Must not be null but may be empty.</param>
/// <param name="UserPrompt">User prompt. Required, non-empty.</param>
/// <param name="Model">Model name (for example "claude-sonnet-4-20250514", "gpt-4.1"). Required.</param>
/// <param name="Temperature">Sampling temperature. Default 0 (deterministic).</param>
/// <param name="MaxTokens">Output token limit. Default 4096.</param>
/// <param name="JsonSchema">JSON schema (optional) for structured output. Null = free-form text.</param>
public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    string Model,
    double Temperature = 0.0,
    int MaxTokens = 4096,
    string? JsonSchema = null)
{
    /// <summary>
    /// Validates required values. Throws <see cref="System.ArgumentException"/> if invalid.
    /// </summary>
    public void Validate()
    {
        if (SystemPrompt is null)
        {
            throw new System.ArgumentException("SystemPrompt must not be null.", nameof(SystemPrompt));
        }

        if (string.IsNullOrWhiteSpace(UserPrompt))
        {
            throw new System.ArgumentException("UserPrompt must not be null or whitespace.", nameof(UserPrompt));
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new System.ArgumentException("Model must not be null or whitespace.", nameof(Model));
        }

        if (Temperature is < 0.0 or > 2.0)
        {
            throw new System.ArgumentException("Temperature must be in [0, 2].", nameof(Temperature));
        }

        if (MaxTokens <= 0)
        {
            throw new System.ArgumentException("MaxTokens must be positive.", nameof(MaxTokens));
        }
    }
}
