// Epic E1 — DI helpers for the Tools module. Hosts that don't use ModuleLoader can call
// AddTools() directly; the canonical wiring is via ToolsModule discovery.

using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentOs.Modules.Tools;

public static class ToolsServiceCollectionExtensions
{
    /// <summary>Register the in-memory <see cref="IToolRegistry"/>. Idempotent.</summary>
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        services.TryAddSingleton<IToolRegistry, InMemoryToolRegistry>();
        return services;
    }

    /// <summary>
    /// Register a single tool implementation. The tool is discovered by <see cref="ToolsModule"/>
    /// at startup and added to the registry.
    /// </summary>
    public static IServiceCollection AddTool<TTool>(this IServiceCollection services)
        where TTool : class, ITool
    {
        services.AddSingleton<ITool, TTool>();
        return services;
    }
}
