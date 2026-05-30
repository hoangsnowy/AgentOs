// Epic E1 — Per-call tool invocation output. The orchestrator turns this into a tool_result
// block the LLM consumes on the next turn. IsError=true surfaces as the provider's error
// shape (Anthropic `is_error: true`, OpenAI `role: tool` with error content).

namespace AgentOs.Domain.Tools;

/// <summary>Result returned from <see cref="ITool.InvokeAsync"/>.</summary>
/// <param name="CallId">Echoes <see cref="ToolInvocationRequest.CallId"/> so the orchestrator can match the response.</param>
/// <param name="Output">Stringified output (typically JSON; plain text allowed). Required, may be empty string.</param>
/// <param name="IsError">True when the tool ran but the operation failed semantically (file not found, build failed, etc).</param>
/// <param name="ErrorMessage">Human-readable error description when <see cref="IsError"/> is true. Optional otherwise.</param>
public sealed record ToolInvocationResult(
    string CallId,
    string Output,
    bool IsError = false,
    string? ErrorMessage = null)
{
    /// <summary>Convenience factory for a successful invocation.</summary>
    public static ToolInvocationResult Success(string callId, string output) =>
        new(callId, output);

    /// <summary>Convenience factory for a tool-side error (the tool ran but failed semantically).</summary>
    public static ToolInvocationResult Error(string callId, string errorMessage) =>
        new(callId, string.Empty, IsError: true, ErrorMessage: errorMessage);
}
