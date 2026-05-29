// AgenticSdlc.Infrastructure/Agents/DependencyInjection.cs
// Phase 4 — Registers the 5 agents + PipelineOrchestrator + AgentsOptions + PipelineOptions into DI.

using AgenticSdlc.Modules.Pipeline.Agents;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Modules.Pipeline.Persistence;
using AgenticSdlc.Modules.Pipeline.Pipeline;
using AgenticSdlc.Modules.Pipeline.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AgenticSdlc.Modules.Pipeline.Agents;

/// <summary>DI extension for the agents layer (call after <c>AddLlmGateway</c>).</summary>
public static class AgentsServiceCollectionExtensions
{
    /// <summary>
    /// Register:
    /// <list type="bullet">
    ///   <item>Bind <see cref="AgentsOptions"/> + <see cref="PipelineOptions"/>.</item>
    ///   <item>4 specialist agents (Requirement / Coding / Testing / QA).</item>
    ///   <item><see cref="PipelineOrchestrator"/> implements <see cref="IOrchestratorAgent"/>.</item>
    ///   <item><see cref="IPipelineProgressSink"/> defaults to no-op (a realtime host overrides it later).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddAgents(this IServiceCollection services, IConfiguration configuration)
    {
        System.ArgumentNullException.ThrowIfNull(services);
        System.ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AgentsOptions>()
            .Bind(configuration.GetSection(AgentsOptions.SectionName));
        services.AddOptions<PipelineOptions>()
            .Bind(configuration.GetSection(PipelineOptions.SectionName));

        services.AddTransient<IRequirementAgent, RequirementAgent>();
        services.AddTransient<ICodingAgent, CodingAgent>();
        services.AddTransient<ITestingAgent, TestingAgent>();
        services.AddTransient<IQaAgent, QaAgent>();

        // IOrchestratorAgent = PersistingOrchestratorAgent wrapping PipelineOrchestrator.
        // Persists run + metrics best-effort (no-op if the DB is not configured via AddPersistence).
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<PipelineOrchestrator>();
        services.AddTransient<MafWorkflowOrchestrator>();   // platform-v2: MAF Workflows graph engine
        services.AddTransient<IOrchestratorAgent>(sp =>
        {
            var engine = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PipelineOptions>>().Value.Engine;
            IOrchestratorAgent inner = string.Equals(engine, "Workflow", System.StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<MafWorkflowOrchestrator>()
                : sp.GetRequiredService<PipelineOrchestrator>();
            return new PersistingOrchestratorAgent(
                inner,
                sp.GetRequiredService<IPipelineRunRepository>(),
                sp.GetRequiredService<IMetricsCollector>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<PersistingOrchestratorAgent>>());
        });

        // Defaults to no-op — a host needing realtime (Blazor) overrides it with a scoped registration after AddAgents.
        services.TryAddSingleton<IPipelineProgressSink>(NullPipelineProgressSink.Instance);

        return services;
    }
}
