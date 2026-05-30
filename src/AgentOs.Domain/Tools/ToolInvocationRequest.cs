// Epic E1 — Per-call tool invocation input. Carries the model-emitted JSON payload + correlation
// metadata so downstream evidence/audit (E5) can stitch every tool call back to its pipeline run.

namespace AgentOs.Domain.Tools;

/// <summary>
/// Input passed to <see cref="ITool.InvokeAsync"/>. <see cref="Input"/> is the model-emitted
/// JSON payload; <see cref="CallId"/> identifies the specific call so a response can be
/// matched back to the originating <c>tool_use</c> block.
/// </summary>
/// <param name="ToolName">Tool the orchestrator resolved to invoke. Required, must match a registered <see cref="ToolDefinition.Name"/>.</param>
/// <param name="CallId">Per-call identifier (typically the model-emitted tool_use id, e.g. Anthropic <c>toolu_*</c>). Required.</param>
/// <param name="Input">JSON object payload validated against <see cref="ToolDefinition.JsonInputSchema"/>. Required.</param>
/// <param name="TenantId">Calling tenant for policy + evidence partitioning. Required.</param>
/// <param name="RunId">Pipeline run correlator. Optional — tool calls outside a pipeline run (e.g. CLI probes) leave this null.</param>
public sealed record ToolInvocationRequest(
    string ToolName,
    string CallId,
    string Input,
    string TenantId,
    string? RunId = null)
{
    /// <summary>Validates required fields. Throws <see cref="System.ArgumentException"/> if invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ToolName))
        {
            throw new System.ArgumentException("ToolName must not be null or whitespace.", nameof(ToolName));
        }

        if (string.IsNullOrWhiteSpace(CallId))
        {
            throw new System.ArgumentException("CallId must not be null or whitespace.", nameof(CallId));
        }

        if (Input is null)
        {
            throw new System.ArgumentException("Input must not be null (use \"{}\" for an empty object).", nameof(Input));
        }

        if (string.IsNullOrWhiteSpace(TenantId))
        {
            throw new System.ArgumentException("TenantId must not be null or whitespace.", nameof(TenantId));
        }
    }
}
