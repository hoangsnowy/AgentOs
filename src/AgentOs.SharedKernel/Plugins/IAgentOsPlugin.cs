// A runtime-discovered extension. Discovered from a plugin assembly dropped in the host's plugins
// folder, NOT compile-time referenced. Extends IModule so a plugin has the SAME DI surface as a
// first-party module (register tools, LLM providers, services, even endpoints) — the host wires it
// through the existing ModuleLoader machinery. The Manifest is what the desktop Plugins manager shows.

using AgentOs.SharedKernel.Modularity;

namespace AgentOs.SharedKernel.Plugins;

/// <summary>Contract a third-party plugin implements. Must have a public parameterless constructor
/// (it is instantiated reflectively at load time, before the DI container exists).</summary>
public interface IAgentOsPlugin : IModule
{
    /// <summary>Identity + metadata for this plugin.</summary>
    PluginManifest Manifest { get; }
}
