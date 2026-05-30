// Epic E1 — Tools module entry. Registers IToolRegistry as a singleton (registry state must
// outlive a single request scope so MCP probes and the orchestrator share one view). Tools
// themselves are discovered via ITool DI registrations and pumped into the registry on startup.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Registry;
using AgentOs.SharedKernel.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentOs.Modules.Tools;

public sealed class ToolsModule : IModule, IInitializableModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IToolRegistry, InMemoryToolRegistry>();
    }

    public Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetRequiredService<IToolRegistry>();
        foreach (var tool in services.GetServices<ITool>())
        {
            registry.Register(tool);
        }

        return Task.CompletedTask;
    }
}
