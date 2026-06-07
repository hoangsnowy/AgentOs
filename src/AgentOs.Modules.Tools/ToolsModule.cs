// Epic E1 — Tools module entry. Registers IToolRegistry as a singleton (registry state must
// outlive a single request scope so MCP probes and the orchestrator share one view). Tools
// themselves are discovered via ITool DI registrations and pumped into the registry on startup.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Tools.Evidence;
using AgentOs.Modules.Tools.Persistence;
using AgentOs.Modules.Tools.Policy;
using AgentOs.Modules.Tools.Registry;
using AgentOs.SharedKernel.Modularity;
using AgentOs.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
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
        // Per-tenant tool allowlist read from AppConfig. Default-permissive until an admin turns on
        // enforcement in the Policy app — UNLESS `Tools:EnforceByDefault` is true, which flips the global
        // posture to fail-closed (deny unless a tenant has an explicit allowlist). Production should set it.
        // IAppConfigStore is optional so this degrades to permissive when no config store is wired
        // (unit tests / no-DB standalone), regardless of the default.
        var enforceByDefault = configuration.GetValue("Tools:EnforceByDefault", false);
        services.TryAddSingleton<IToolPolicy>(sp => new Policy.AppConfigToolPolicy(
            sp.GetService<AgentOs.Modules.AppConfig.IAppConfigStore>(), enforceByDefault));
        services.TryAddSingleton<IToolPolicyService, Policy.ToolPolicyService>();

        // Evidence sink: durable EF-backed when a DB is configured, else the in-memory ring buffer
        // (dev / CI). The schema `tools` keeps tool-invocation evidence as a first-class audit trail.
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        // The inner sink is the real persistence (durable EF when a DB is wired, else the in-memory ring).
        // It is wrapped by BufferedToolInvocationLog so the gateway's AppendAsync is a non-blocking enqueue
        // (M6 perf — keeps per-tool-call DB writes off the run's critical path; drained on a background loop
        // started in InitializeAsync).
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<ToolsDbContext>(opt =>
                opt.UseNpgsql(connectionString, npg =>
                    npg.MigrationsHistoryTable("__EFMigrationsHistory", schema: "tools")));
            services.AddNpgsqlConnectionFactory(connectionString);
            services.TryAddSingleton<EfToolInvocationLog>();
            services.TryAddSingleton<IToolInvocationLog>(sp =>
                new BufferedToolInvocationLog(sp.GetRequiredService<EfToolInvocationLog>()));
        }
        else
        {
            services.TryAddSingleton<InMemoryToolInvocationLog>();
            services.TryAddSingleton<IToolInvocationLog>(sp =>
                new BufferedToolInvocationLog(sp.GetRequiredService<InMemoryToolInvocationLog>()));
        }

        // M1 — the shared policy-gate + invoke + evidence seam. Every governed execution path
        // (the in-process LLM tool loop and the remote-session executor) resolves this so tools
        // are gated + recorded identically regardless of where the side effect runs.
        services.TryAddSingleton<IToolGateway>(sp => new DefaultToolGateway(
            sp.GetService<IToolPolicy>(),
            sp.GetService<IToolInvocationLog>()));
    }

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetRequiredService<IToolRegistry>();
        foreach (var tool in services.GetServices<ITool>())
        {
            registry.Register(tool);
        }

        // Start the single background drain loop for the buffered evidence log.
        (services.GetService<IToolInvocationLog>() as BufferedToolInvocationLog)?.StartDraining();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetService<ToolsDbContext>();
        if (db is not null)
        {
            await db.Database.MigrateAsync(ct).ConfigureAwait(false);
        }
    }
}
