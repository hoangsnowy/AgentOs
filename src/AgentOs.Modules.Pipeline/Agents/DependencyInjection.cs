// AgentOs.Infrastructure/Agents/DependencyInjection.cs
// Phase 4 — Registers the 5 agents + PipelineOrchestrator + AgentsOptions + PipelineOptions into DI.

using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Pipeline;
using AgentOs.Modules.Pipeline.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AgentOs.Modules.Pipeline.Agents;

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
        services.AddTransient<AgentOs.Domain.Sessions.IIssueWorkAgent, IssueWorkAgent>();
        services.AddTransient<ICodingAgent, CodingAgent>();
        services.AddTransient<ITestingAgent, TestingAgent>();
        services.AddTransient<IQaAgent, QaAgent>();

        // Workflow graph executor: runs an OrchestrationStudio graph against these typed agents.
        services.AddScoped<GraphExecution.GraphExecutor>();

        // Bootstrap (idea → board tickets): the decomposer agent + the idea→preview service.
        services.AddTransient<Decomposition.ITicketDecomposerAgent, Decomposition.TicketDecomposerAgent>();
        services.AddTransient<Decomposition.IIdeaBootstrapService, Decomposition.IdeaBootstrapService>();

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
                sp.GetRequiredService<AgentOs.Domain.Cost.IBudgetGuard>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<PersistingOrchestratorAgent>>());
        });

        // Defaults to no-op — a host needing realtime (Blazor) overrides it with a scoped registration after AddAgents.
        services.TryAddSingleton<IPipelineProgressSink>(NullPipelineProgressSink.Instance);

        return services;
    }
}
