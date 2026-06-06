// Identity + metadata a plugin advertises about itself. Surfaced in the desktop Plugins manager.

namespace AgentOs.SharedKernel.Plugins;

/// <summary>Self-describing metadata for an <see cref="IAgentOsPlugin"/>.</summary>
/// <param name="Id">Stable, unique plugin id (e.g. <c>"agentos.sample.tools"</c>). Used for de-dup + display.</param>
/// <param name="Name">Human-readable display name.</param>
/// <param name="Version">Semantic version string (e.g. <c>"1.0.0"</c>).</param>
/// <param name="Author">Optional author / vendor.</param>
/// <param name="Description">Optional one-line description of what the plugin contributes.</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string? Author = null,
    string? Description = null);
