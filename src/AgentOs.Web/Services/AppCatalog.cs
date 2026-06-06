// AgentOs.Web/Services/AppCatalog.cs
// Single source of truth for the desktop's launchable apps. The Start menu, dock and
// desktop icons all read from here so a new app is registered in exactly one place.

using System.Collections.Generic;
using System.Linq;

namespace AgentOs.Web.Services;

/// <summary>A launchable desktop application.</summary>
/// <param name="Key">Stable id used by <see cref="WindowManagerService"/> and recents.</param>
/// <param name="Title">Window + launcher title.</param>
/// <param name="Icon">Icon name (see <c>Icon.razor</c> map).</param>
/// <param name="Caption">One-line description shown in the launcher.</param>
/// <param name="Category">Grouping bucket in the Start menu.</param>
/// <param name="W">Default window width.</param>
/// <param name="H">Default window height.</param>
/// <param name="Pinned">Whether the app appears in the dock + pinned grid.</param>
/// <param name="AdminOnly">When true the app is only shown to users holding the tenant <c>admin</c> role.</param>
/// <param name="ComponentType">For plugin-contributed apps: the Blazor component the WindowHost renders
/// via <c>DynamicComponent</c>. Null for built-in apps (rendered by the WindowHost switch).</param>
public sealed record DesktopApp(
    string Key,
    string Title,
    string Icon,
    string Caption,
    string Category,
    int W = 920,
    int H = 620,
    bool Pinned = true,
    bool AdminOnly = false,
    System.Type? ComponentType = null);

/// <summary>The catalog of apps the AgentOS shell can launch — built-ins plus any registered by plugins.</summary>
public static class AppCatalog
{
    private static readonly List<DesktopApp> _builtIn = new()
    {
        new("pipeline", "Pipeline", "play",  "Run the 5-agent SDLC pipeline", "Agents", 920, 620),
        new("workflow", "Workflow", "graph", "Visual orchestration editor",   "Agents", 1080, 660),
        new("spine",    "Spine",    "git-pull-request", "Boards → tickets → AI sessions", "Agents", 940, 640),
        new("users",    "Users",    "user",  "Manage tenant members + roles", "Admin", 880, 600, Pinned: true, AdminOnly: true),
        new("evidence", "Evidence", "lock",  "Tool-invocation audit trail",   "Admin", 960, 620, Pinned: true, AdminOnly: true),
        new("cost",     "Cost",     "graph", "LLM spend by agent, provider, model", "Admin", 1000, 660, Pinned: true, AdminOnly: true),
        new("plugins",  "Plugins",  "squares-stack", "Installed extensions",  "System", 860, 600),
        new("settings", "Settings", "gear",  "LLM keys, providers, GitHub",   "System", 760, 600),
        new("system",   "System",   "wrench","OS appearance, themes, about",  "System", 760, 600),
    };

    // Plugin-contributed apps, appended once at host startup (before any request is served).
    private static readonly List<DesktopApp> _plugin = new();

    /// <summary>Register plugin-contributed desktop apps (idempotent by key). Call once at startup.</summary>
    public static void RegisterPluginApps(IEnumerable<DesktopApp> apps)
    {
        foreach (var app in apps)
        {
            if (!_builtIn.Any(a => a.Key == app.Key) && !_plugin.Any(a => a.Key == app.Key))
            {
                _plugin.Add(app);
            }
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
