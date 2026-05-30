// Epic E1 — Tool registry. Holds the set of tools available to agents at runtime. Implementations
// are expected to be thread-safe so MCP client probes + the orchestrator can read/mutate
// concurrently. The default in-memory implementation lives in AgentOs.Modules.Tools; future
// implementations can hydrate from per-tenant config (E5) or remote MCP catalogs (E3).

namespace AgentOs.Domain.Tools;

/// <summary>Lookup + management surface for the live set of <see cref="ITool"/>s.</summary>
public interface IToolRegistry
{
    /// <summary>Register a tool. Throws <see cref="ToolException"/> if a tool with the same <see cref="ToolDefinition.Name"/> already exists.</summary>
    void Register(ITool tool);

    /// <summary>Remove a previously registered tool. Returns false if nothing was registered under that name.</summary>
    bool Unregister(string toolName);

    /// <summary>Returns the registered tool or null when no tool matches <paramref name="toolName"/>.</summary>
    ITool? Resolve(string toolName);

    /// <summary>Snapshot of every currently registered tool's definition. Used by the orchestrator when building the LLM tools block.</summary>
    System.Collections.Generic.IReadOnlyList<ToolDefinition> List();
}
