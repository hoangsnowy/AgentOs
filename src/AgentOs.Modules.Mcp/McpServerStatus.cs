// Read-only status surface for the MCP admin app: which configured upstream servers connected at
// startup, how many tools each contributed, and why a connect failed. Runtime CRUD of server
// config is deliberately out of scope (config is appsettings-bound; edit + restart).

namespace AgentOs.Modules.Mcp;

/// <summary>One configured upstream MCP server's startup outcome.</summary>
public sealed record McpServerStatus(
    string Name,
    string Transport,
    bool Enabled,
    bool Connected,
    int ToolCount,
    string? Error);
