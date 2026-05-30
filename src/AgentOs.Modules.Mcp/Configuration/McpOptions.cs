// Epic E3 — MCP client config. Bound from the "Mcp" section of appsettings/user-secrets. Each
// Servers[] entry maps to one external MCP server AgentOs connects to at startup; the server's
// listed tools are wrapped into McpToolAdapter ITools and registered in the IToolRegistry.

using System.Collections.Generic;

namespace AgentOs.Modules.Mcp.Configuration;

/// <summary>Root MCP options. Bound to <c>"Mcp"</c> in configuration.</summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>Configured upstream MCP servers. Empty = MCP module no-ops at startup.</summary>
    public IList<McpServerOptions> Servers { get; init; } = new List<McpServerOptions>();

    /// <summary>
    /// Per-tool-call timeout in seconds; default 60. Caps how long the host waits on
    /// <c>tools/call</c> before cancelling and returning a tool_result error to the agent.
    /// </summary>
    public int CallTimeoutSeconds { get; init; } = 60;
}

/// <summary>One upstream MCP server entry.</summary>
public sealed class McpServerOptions
{
    /// <summary>Stable name used as the prefix on every registered tool (<c>{name}.{tool}</c>). Required.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Transport kind: <c>stdio</c> (subprocess) or <c>http</c> (SSE/Streamable HTTP). Required.</summary>
    public string Transport { get; init; } = "stdio";

    /// <summary>Stdio only — executable to spawn (e.g. <c>npx</c>, <c>dotnet</c>).</summary>
    public string? Command { get; init; }

    /// <summary>Stdio only — process arguments.</summary>
    public IList<string> Args { get; init; } = new List<string>();

    /// <summary>Stdio only — extra environment variables for the subprocess.</summary>
    public IDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();

    /// <summary>HTTP only — server URL (SSE or Streamable HTTP endpoint).</summary>
    public string? Url { get; init; }

    /// <summary>Disabled servers stay in config but the host skips connecting them at startup.</summary>
    public bool Enabled { get; init; } = true;
}
