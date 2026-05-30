// Epic E5 — Pre-invocation policy gate. Every tool call that reaches the gateway is asked
// "may this tenant invoke this tool with this input?" before the actual InvokeAsync runs.
// The default policy is permissive; production policies will read a per-tenant allowlist /
// per-tool cost cap from AppConfig.

namespace AgentOs.Domain.Tools;

/// <summary>Pre-invocation policy gate for tools.</summary>
public interface IToolPolicy
{
    /// <summary>Decide whether the given tool invocation may proceed.</summary>
    System.Threading.Tasks.Task<ToolPolicyDecision> EvaluateAsync(
        ToolInvocationRequest request,
        System.Threading.CancellationToken cancellationToken = default);
}

/// <summary>Result of a policy evaluation.</summary>
/// <param name="Allowed">True = invocation proceeds. False = invocation is short-circuited with <see cref="Reason"/> surfaced to the model.</param>
/// <param name="Reason">Human-readable reason (always present when <see cref="Allowed"/> is false).</param>
public sealed record ToolPolicyDecision(bool Allowed, string? Reason = null)
{
    /// <summary>Allow with no reason recorded.</summary>
    public static readonly ToolPolicyDecision Allow = new(true);

    /// <summary>Deny with a reason — the reason is fed back to the LLM as the tool_result error.</summary>
    public static ToolPolicyDecision Deny(string reason) => new(false, reason);
}
