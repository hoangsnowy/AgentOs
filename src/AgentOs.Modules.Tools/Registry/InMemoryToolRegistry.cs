// Epic E1 — Default IToolRegistry implementation. Thread-safe via ConcurrentDictionary so
// startup population (ToolsModule.InitializeAsync) and concurrent orchestrator reads don't race.
// MCP integration (E3) writes into the same registry as MCP servers connect/disconnect.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AgentOs.Domain.Tools;

namespace AgentOs.Modules.Tools.Registry;

internal sealed class InMemoryToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, ITool> _tools = new(StringComparer.Ordinal);

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var definition = tool.Definition;
        definition.Validate();

        if (!_tools.TryAdd(definition.Name, tool))
        {
            throw new ToolException($"A tool named '{definition.Name}' is already registered.");
        }
    }

    public bool Unregister(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return _tools.TryRemove(toolName, out _);
    }

    public ITool? Resolve(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        return _tools.TryGetValue(toolName, out var tool) ? tool : null;
    }

    public IReadOnlyList<ToolDefinition> List() =>
        _tools.Values.Select(t => t.Definition).ToArray();
}
