// AgentOs.Web/Services/AppCatalog.cs
// Single source of truth for the desktop's launchable apps. The Start menu, dock and
// desktop icons all read from here so a new app is registered in exactly one place.

using System.Collections.Generic;
using System.Linq;
using AgentOs.Web.Components.Pages;
using AgentOs.Web.Shell.Services;

namespace AgentOs.Web.Services;

/// <summary>The catalog of apps the AgentOS shell can launch — built-ins plus any registered by plugins.</summary>
public static class AppCatalog
{
    private static readonly List<DesktopApp> _builtIn = new()
    {
        new("home", "Home", "house", "Start here: idea -> spec -> code -> test -> PR", "Agents", 940, 640, Pinned: true, ComponentType: typeof(HomeApp), Color: "#3584e4"),
        new("pipeline", "Pipeline", "play",  "Run the 5-agent pipeline on a sandbox story", "Agents", 920, 620, ComponentType: typeof(PipelineStudio), Color: "#3584e4"),
        new("workflow", "Workflow", "graph", "Edit the same pipeline as a visual graph",     "Agents", 1080, 660, ComponentType: typeof(OrchestrationStudio), Color: "#9141ac"),
        new("spine",    "Spine",    "git-pull-request", "Run the pipeline on a real ticket → PR", "Agents", 940, 640, ComponentType: typeof(SpineApp), Color: "#2ec27e"),
        new("workspaces", "Workspaces", "squares-four", "Connected boards + their repos", "Agents", 940, 620, Pinned: false, ComponentType: typeof(WorkspacesApp), Color: "#e66100"),
        new("sessions",   "Sessions",   "clock-counter", "Runners + AI coding sessions",  "Agents", 940, 620, Pinned: false, ComponentType: typeof(SessionsApp), Color: "#986a44"),
        new("users",    "Users",    "user",  "Manage tenant members + roles", "Admin", 880, 600, Pinned: true, AdminOnly: true, ComponentType: typeof(UsersApp), Color: "#e5a50a"),
        new("evidence", "Evidence", "lock",  "Tool-invocation audit trail",   "Admin", 960, 620, Pinned: true, AdminOnly: true, ComponentType: typeof(EvidenceApp), Color: "#c01c28"),
        new("cost",     "Cost",     "graph", "LLM spend by agent, provider, model", "Admin", 1000, 660, Pinned: true, AdminOnly: true, ComponentType: typeof(CostApp), Color: "#1c9099"),
        new("policy",   "Policy",   "lock",  "Per-tenant tool allowlist",     "Admin", 900, 620, Pinned: true, AdminOnly: true, ComponentType: typeof(PolicyApp), Color: "#613583"),
        new("prompts",  "Prompts",  "lightning", "Tune the agents' prompts",   "Admin", 1000, 680, Pinned: true, AdminOnly: true, ComponentType: typeof(PromptsApp), Color: "#c061cb"),
        new("plugins",  "Plugins",  "squares-stack", "Installed extensions",  "System", 860, 600, ComponentType: typeof(PluginsApp), Color: "#5e5c64"),
        new("mcp",      "MCP",      "arrow-square-out", "Upstream MCP tool servers", "System", 860, 600, Pinned: false, AdminOnly: true, ComponentType: typeof(McpApp), Color: "#1a5fb4"),
        new("settings", "Settings", "gear",  "LLM keys, providers, GitHub",   "System", 760, 600, Pinned: true, AdminOnly: true, ComponentType: typeof(Settings), Color: "#5e5c64"),
        new("system",   "System",   "wrench","OS appearance, themes, about",  "System", 760, 600, ComponentType: typeof(SystemApp), Color: "#3d3846"),
    };

    // Plugin-contributed apps, appended once at host startup (before any request is served).
    private static readonly List<DesktopApp> _plugin = new();

    /// <summary>Register plugin-contributed desktop apps (idempotent by key). Call once at startup.</summary>
    public static void RegisterPluginApps(IEnumerable<DesktopApp> apps)
    {
        foreach (var app in apps.Where(app =>
            !_builtIn.Any(a => a.Key == app.Key) && !_plugin.Any(a => a.Key == app.Key)))
        {
            _plugin.Add(app);
        }
    }

    /// <summary>All registered apps, in display order (built-ins first, then plugin apps).</summary>
    public static IReadOnlyList<DesktopApp> All => _builtIn.Concat(_plugin).ToList();

    /// <summary>Apps that appear in the dock and the pinned grid.</summary>
    public static IReadOnlyList<DesktopApp> Pinned => All.Where(a => a.Pinned).ToList();

    /// <summary>Resolve an app by key, or <c>null</c> if unknown.</summary>
    public static DesktopApp? Find(string key) =>
        All.FirstOrDefault(a => a.Key == key);

    /// <summary>All apps visible to a user with the given admin status (filters out <see cref="DesktopApp.AdminOnly"/>).</summary>
    public static IEnumerable<DesktopApp> VisibleAll(bool isAdmin) =>
        All.Where(a => isAdmin || !a.AdminOnly);

    /// <summary>Pinned apps visible to a user with the given admin status.</summary>
    public static IEnumerable<DesktopApp> VisiblePinned(bool isAdmin) =>
        Pinned.Where(a => isAdmin || !a.AdminOnly);
}
