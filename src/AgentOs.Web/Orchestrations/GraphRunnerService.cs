// Web-side driver for the OrchestrationStudio graph executor. Maps the canvas OrchestrationGraph into the
// provider-neutral PlanGraph the Pipeline-module executor understands, validates it (LLM-free), and runs it
// against the real agents. Scoped + side-effect-free ctor (eager @inject on window open must not throw).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.GraphExecution;
using Microsoft.Extensions.DependencyInjection;

namespace AgentOs.Web.Orchestrations;

/// <summary>Validates + runs an <see cref="OrchestrationGraph"/> via the typed-agent <see cref="GraphExecutor"/>.</summary>
public sealed class GraphRunnerService
{
    // Resolve the executor (which constructs the agents -> an LLM client) LAZILY, inside RunAsync. The
    // ctor MUST stay side-effect-free: this service is @injected into OrchestrationStudio, so it is built
    // eagerly when that window opens; constructing the agents there crashes the circuit (eager-DI rule).
    private readonly IServiceProvider _services;

    public GraphRunnerService(IServiceProvider services) => _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>Map the canvas graph to the executor's provider-neutral DTO.</summary>
    public static PlanGraph ToPlan(OrchestrationGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var nodes = graph.Nodes
            .Select(n => new PlanNode(n.Id, n.Type.ToString(), n.AgentRole, n.Title, n.MaxIterations, n.IsStart))
            .ToList();
        var edges = graph.Edges
            .Select(e => new PlanEdge(e.Id, e.SourceId, e.TargetId, e.Label))
            .ToList();
        return new PlanGraph(graph.Id, graph.Name, nodes, edges);
    }

    /// <summary>LLM-free validation — drives the pre-run gate + per-node Skipped status.</summary>
    public static GraphValidationResult Validate(OrchestrationGraph graph) => GraphPlanner.Plan(ToPlan(graph));

    /// <summary>Run the graph against the real agents, pushing per-node status to <paramref name="onNode"/>.</summary>
    public Task<GraphRunResult> RunAsync(
        OrchestrationGraph graph, string userStoryText, int nMax, Func<GraphNodeEvent, Task> onNode, CancellationToken ct)
    {
        var executor = _services.GetRequiredService<GraphExecutor>(); // lazy: constructs the agents only at run time
        return executor.RunAsync(ToPlan(graph), userStoryText, nMax, onNode, ct);
    }
}
