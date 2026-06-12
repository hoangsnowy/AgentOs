// Epic E3 — MCP module entry. Binds McpOptions, registers McpClientHost as a singleton, and in
// the IInitializableModule phase connects to every configured server and pumps its tools into the
// IToolRegistry. Depends on IToolRegistry being registered first (ToolsModule).

using System;
using System.Linq;
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

        // Fail-fast config: a server entry missing its name or transport coordinates would otherwise
        // surface as an opaque connect failure at startup (or a silently dead toolset).
        services.AddOptions<McpOptions>()
            .Bind(configuration.GetSection(McpOptions.SectionName))
            .Validate(o => o.CallTimeoutSeconds > 0,
                "Mcp:CallTimeoutSeconds must be > 0.")
            .Validate(o => o.Servers.All(s => !s.Enabled || !string.IsNullOrWhiteSpace(s.Name)),
                "Mcp:Servers — every enabled server needs a Name (it prefixes its tools).")
            .Validate(o => o.Servers.All(s => !s.Enabled
                    || !string.Equals(s.Transport, "stdio", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(s.Command)),
                "Mcp:Servers — a stdio server needs a Command to spawn.")
            .Validate(o => o.Servers.All(s => !s.Enabled
                    || !string.Equals(s.Transport, "http", StringComparison.OrdinalIgnoreCase)
                    || Uri.TryCreate(s.Url, UriKind.Absolute, out _)),
                "Mcp:Servers — an http server needs an absolute Url.")
            .ValidateOnStart();

        services.AddSingleton<McpClientHost>();
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        var host = services.GetRequiredService<McpClientHost>();
        await host.ConnectAllAsync(ct).ConfigureAwait(false);
    }
}
