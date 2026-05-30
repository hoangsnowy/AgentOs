// Epic E1 — Tool metadata. Immutable description of one tool: the name agents reference, a
// model-facing description, and a JSON Schema for the input payload. The schema is the contract
// the LLM serializes against and the registry validates against before invoking the tool.

namespace AgentOs.Domain.Tools;

/// <summary>Static metadata for a tool. Returned from <see cref="ITool.Definition"/>.</summary>
/// <param name="Name">
/// Stable identifier the agent uses to invoke the tool. Required, non-empty, must be unique
/// within a registry. Conventionally snake_case (matches MCP + Anthropic tools convention).
/// </param>
/// <param name="Description">
/// Human-readable description the LLM sees when deciding whether to call the tool. Required.
/// </param>
/// <param name="JsonInputSchema">
/// JSON Schema (draft 2020-12) describing the input object. Required. Validated against
/// <see cref="ToolInvocationRequest.Input"/> before <see cref="ITool.InvokeAsync"/> runs.
/// </param>
public sealed record ToolDefinition(
    string Name,
    string Description,
    string JsonInputSchema)
{
    /// <summary>Validates required fields. Throws <see cref="System.ArgumentException"/> if invalid.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new System.ArgumentException("Name must not be null or whitespace.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new System.ArgumentException("Description must not be null or whitespace.", nameof(Description));
        }

        if (string.IsNullOrWhiteSpace(JsonInputSchema))
        {
            throw new System.ArgumentException("JsonInputSchema must not be null or whitespace.", nameof(JsonInputSchema));
        }
    }
}
