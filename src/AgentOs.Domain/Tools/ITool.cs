// Epic E1 — Tool contract. A tool is a callable capability an agent can invoke during a run
// (read a file, run a build, call an MCP server, hit GitHub, etc). Provider-neutral: lives in
// Domain so any module (Integration, Mcp, custom) can implement it without referencing the
// runtime. The pipeline LlmRequest translator maps the registered tools' JsonInputSchema into
// the provider-specific tools block (Anthropic `tools`, OpenAI `tools`, MCP tool listing).

namespace AgentOs.Domain.Tools;

/// <summary>A callable capability invocable by an agent during a pipeline run.</summary>
public interface ITool
{
    /// <summary>Static metadata used for registration, discovery and prompt-side translation.</summary>
    ToolDefinition Definition { get; }

    /// <summary>
    /// Invoke the tool. Implementations must be safe to call concurrently; per-invocation state
    /// belongs in <paramref name="request"/>. Throw <see cref="ToolException"/> for permanent
    /// failures the orchestrator should surface to the model as a tool_result error.
    /// </summary>
    System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        System.Threading.CancellationToken cancellationToken = default);
}
