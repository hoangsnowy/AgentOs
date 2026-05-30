// Epic E3 — MCP module entry. Binds McpOptions, registers McpClientHost as a singleton, and in
// the IInitializableModule phase connects to every configured server and pumps its tools into the
// IToolRegistry. Depends on IToolRegistry being registered first (ToolsModule).

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Mcp.Configuration;
using AgentOs.SharedKernel.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOs.Modules.Mcp;

public sealed class McpModule : IModule, IInitializableModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<McpOptions>()
            .Bind(configuration.GetSection(McpOptions.SectionName));

        services.AddSingleton<McpClientHost>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        var host = services.GetRequiredService<McpClientHost>();
        await host.ConnectAllAsync(ct).ConfigureAwait(false);
    }
}
