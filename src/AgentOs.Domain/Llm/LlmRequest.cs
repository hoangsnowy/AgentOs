// AgentOs.Domain/Llm/LlmRequest.cs
// Sprint 1 — LLM Gateway contract (request side).

namespace AgentOs.Domain.Llm;

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
/// <param name="Tools">
/// Optional tool names the agent is allowed to invoke during this call. The gateway resolves each
/// name via the configured <see cref="AgentOs.Domain.Tools.IToolRegistry"/>, exposes the tool to
/// the provider (Anthropic/OpenAI/MCP), and runs the tool-call loop transparently before returning
/// the final <see cref="LlmResponse"/>. Null/empty = no tools available for this call. Names that
/// aren't registered are silently dropped — the orchestrator never sends an unresolved tool to the
/// model.
/// </param>
/// <param name="Timeout">
/// Optional per-request timeout hint. Provider-neutral: the HTTP-backed clients (Claude / Azure)
/// rely on their own SDK timeouts and ignore this, but <c>RemoteAgentLlmClient</c> honours it as the
/// dispatch deadline — a single agentic CLI run on a paired dev machine (clone → build → test → push)
/// far outlasts the default 120s, so the caller raises it for that path. Null = provider default.
/// </param>
/// <param name="Cli">
/// Optional CLI-agent profile hint (e.g. <c>"claude"</c>, <c>"codex"</c>). Only <c>RemoteAgentLlmClient</c>
/// honours it — it tells the paired runner which subscription CLI to invoke (Claude Code vs OpenAI Codex),
/// so the run uses the member's flat monthly subscription instead of metered API tokens. Null = the
/// runner's configured default. Ignored by the HTTP-backed providers.
/// </param>
public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    string Model,
    double Temperature = 0.0,
    int MaxTokens = 4096,
    string? JsonSchema = null,
    System.Collections.Generic.IReadOnlyList<string>? Tools = null,
    System.TimeSpan? Timeout = null,
    string? Cli = null)
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
