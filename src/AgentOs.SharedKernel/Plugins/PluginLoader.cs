// Runtime plugin discovery. Loads plugin assemblies from a folder (or takes assemblies directly, for
// tests), finds every IAgentOsPlugin, registers it through the SAME machinery as a first-party module
// (AddSingleton<IModule> + AddServices), and records the outcome in an IPluginCatalog the desktop reads.
// A bad plugin is captured as a Failed entry — it never crashes host startup.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Tools;
using AgentOs.SharedKernel.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOs.SharedKernel.Plugins;

/// <summary>Host-side helpers to discover and wire <see cref="IAgentOsPlugin"/> instances at startup.</summary>
public static class PluginLoader
{
    /// <summary>Load every <c>*.dll</c> in <paramref name="pluginsPath"/>, register the plugins each
    /// contains, and register the resulting <see cref="IPluginCatalog"/>. A missing folder is a no-op
    /// (empty catalog). Call once, after the host's compile-time modules are added.</summary>
    public static IServiceCollection AddPlugins(
        this IServiceCollection services,
        IConfiguration configuration,
        string pluginsPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var results = new List<LoadedPlugin>();
        var assemblies = new List<Assembly>();

        if (!string.IsNullOrWhiteSpace(pluginsPath) && Directory.Exists(pluginsPath))
        {
            foreach (var dll in Directory.GetFiles(pluginsPath, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    assemblies.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll)));
                }
#pragma warning disable CA1031 // A broken plugin file must be reported, not crash host startup.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    results.Add(FailedFile(dll, ex));
                }
            }
        }

        RegisterPlugins(services, configuration, assemblies, results);
        services.AddSingleton<IPluginCatalog>(new PluginCatalog(results));
        return services;
    }

    /// <summary>Register plugins from already-loaded assemblies (the testable core). Registers the
    /// resulting <see cref="IPluginCatalog"/>.</summary>
    public static IServiceCollection AddPluginsFromAssemblies(
        this IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(assemblies);

        var results = new List<LoadedPlugin>();
        RegisterPlugins(services, configuration, assemblies, results);
        services.AddSingleton<IPluginCatalog>(new PluginCatalog(results));
        return services;
    }

    private static void RegisterPlugins(
        IServiceCollection services,
        IConfiguration configuration,
        IEnumerable<Assembly> assemblies,
        List<LoadedPlugin> sink)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IAgentOsPlugin).IsAssignableFrom(type))
                {
                    continue;
                }

                // Construct + read the manifest first, so a later AddServices failure still keeps the
                // plugin's real identity in the catalog (not a synthetic type-name placeholder).
                IAgentOsPlugin plugin;
                PluginManifest manifest;
                try
                {
                    plugin = (IAgentOsPlugin)Activator.CreateInstance(type)!;
                    manifest = plugin.Manifest;
                }
#pragma warning disable CA1031 // A plugin that won't even construct must not abort the others.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    sink.Add(new LoadedPlugin(
                        new PluginManifest(type.FullName ?? type.Name, type.Name, "?"),
                        PluginStatus.Failed, assembly.Location, [], ex.Message));
                    continue;
                }

                try
                {
                    // Run the plugin's own registrations first; only on success register it as a module
                    // (so MapModuleEndpoints / InitializeModulesAsync see it) and record the catalog entry.
                    var startIndex = services.Count;
                    plugin.AddServices(services, configuration);
                    var capabilities = SummarizeCapabilities(services, startIndex);
                    services.AddSingleton<IModule>(plugin);

                    sink.Add(new LoadedPlugin(manifest, PluginStatus.Loaded, assembly.Location, capabilities));
                }
#pragma warning disable CA1031 // One plugin throwing must not abort discovery of the others.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    sink.Add(new LoadedPlugin(manifest, PluginStatus.Failed, assembly.Location, [], ex.Message));
                }
            }
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
    }

    private static List<string> SummarizeCapabilities(IServiceCollection services, int startIndex)
    {
        var tools = 0;
        var others = 0;
        var providers = new List<string>();

        for (var i = startIndex; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(ITool))
            {
                tools++;
            }
            else if (descriptor.ServiceType == typeof(ILlmClient) && descriptor.IsKeyedService)
            {
                providers.Add(descriptor.ServiceKey?.ToString() ?? "?");
            }
            else
            {
                others++;
            }
        }

        var caps = new List<string>();
        if (tools > 0)
        {
            caps.Add(tools == 1 ? "1 tool" : $"{tools} tools");
        }
        caps.AddRange(providers.Select(p => $"provider: {p}"));
        if (others > 0)
        {
            caps.Add(others == 1 ? "1 service" : $"{others} services");
        }
        return caps;
    }

    private static LoadedPlugin FailedFile(string dllPath, Exception ex)
    {
        var name = Path.GetFileNameWithoutExtension(dllPath);
        return new LoadedPlugin(
            new PluginManifest(name, name, "?"), PluginStatus.Failed, Path.GetFullPath(dllPath), [], ex.Message);
    }
}
