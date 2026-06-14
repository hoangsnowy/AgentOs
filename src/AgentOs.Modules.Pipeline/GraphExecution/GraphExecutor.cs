// Compiles a validated visual graph into a Microsoft Agent Framework *Workflow* and runs it on the MAF
// in-process runtime — the graph the user draws in the Workflow studio IS a MAF Workflow. One MAF Executor
// per node (id = node id), edges become MAF edges (the QA gate's pass/fail become conditional edges), and
// MAF's own ExecutorInvoked/Completed/Failed event stream drives the live canvas. Node work delegates to the
// real typed agents (Requirement/Coding/Testing/QA), the LLM gateway (Llm), and the governed tool gateway
// (Tool) — so every Tool call still passes IToolPolicy + IToolInvocationLog. Refuses to start a graph that
// isn't runnable (unsupported nodes on the path) before any executor runs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Domain.Tools;
using AgentOs.Modules.Pipeline.Agents;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Pipeline.GraphExecution;

/// <summary>Compiles a <see cref="PlanGraph"/> into a MAF <see cref="Workflow"/> and executes it. Scoped;
/// ctor is side-effect-free (constructing the agents resolves an LLM client, so it stays out of the Web
/// component ctor — this executor is resolved lazily at run time, see <c>GraphRunnerService</c>).</summary>
public sealed class GraphExecutor
{
    // Backstop against a runaway cyclic graph the cap logic somehow doesn't bound.
    private const string OperatorTenant = "operator";

    private readonly IRequirementAgent _requirement;
    private readonly ICodingAgent _coding;
    private readonly ITestingAgent _testing;
    private readonly IQaAgent _qa;
    private readonly ILlmClientFactory _llmFactory;
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolGateway _toolGateway;
    private readonly AgentsOptions _agents;
    private readonly ILogger<GraphExecutor> _logger;

    public GraphExecutor(
        IRequirementAgent requirement,
        ICodingAgent coding,
        ITestingAgent testing,
        IQaAgent qa,
        ILlmClientFactory llmFactory,
        IToolRegistry toolRegistry,
        IToolGateway toolGateway,
        IOptions<AgentsOptions> agents,
        ILogger<GraphExecutor> logger)
    {
        _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        _coding = coding ?? throw new ArgumentNullException(nameof(coding));
        _testing = testing ?? throw new ArgumentNullException(nameof(testing));
        _qa = qa ?? throw new ArgumentNullException(nameof(qa));
        _llmFactory = llmFactory ?? throw new ArgumentNullException(nameof(llmFactory));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _toolGateway = toolGateway ?? throw new ArgumentNullException(nameof(toolGateway));
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Validate (LLM-free) then compile + run the graph as a MAF Workflow, pushing per-node status
    /// to <paramref name="onNode"/>. <paramref name="tenantId"/> partitions tool policy/evidence — the
    /// caller passes it explicitly because a Blazor circuit has no <c>ITenantContext</c>.</summary>
    public async Task<GraphRunResult> RunAsync(
        PlanGraph graph,
        string userStoryText,
        int nMax,
        string tenantId,
        Func<GraphNodeEvent, Task> onNode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(onNode);

        var plan = GraphPlanner.Plan(graph);
        if (!plan.IsRunnable)
        {
            foreach (var v in plan.Nodes.Where(n => n.Support != NodeSupport.Supported))
            {
                await onNode(new GraphNodeEvent(v.NodeId, GraphNodePhase.Skipped, null, v.Reason)).ConfigureAwait(false);
            }
            var reasons = string.Join("; ", plan.Errors
                .Concat(plan.Nodes.Where(n => n.Support != NodeSupport.Supported).Select(n => n.Reason)));
            return new GraphRunResult(false, $"Graph not runnable: {reasons}");
        }

        // Baseline: everything Pending.
        foreach (var n in graph.Nodes)
        {
            await onNode(new GraphNodeEvent(n.Id, GraphNodePhase.Pending, null, null)).ConfigureAwait(false);
        }

        var clampedNMax = Math.Clamp(nMax, 1, 10);
        var runId = Guid.NewGuid().ToString("N");
        var state = new GraphState(userStoryText, clampedNMax, Coalesce(tenantId, OperatorTenant), runId);

        var supported = plan.Nodes
            .Where(v => v.Support == NodeSupport.Supported)
            .Select(v => v.NodeId)
            .ToHashSet(StringComparer.Ordinal);

        Workflow workflow;
        try
        {
            workflow = BuildWorkflow(graph, plan, supported, clampedNMax);
        }
        catch (InvalidOperationException ex)
        {
            return new GraphRunResult(false, ex.Message);
        }

        string? failure = null;
        var run = await InProcessExecution.RunStreamingAsync(workflow, state, runId, ct).ConfigureAwait(false);
        await foreach (var evt in run.WatchStreamAsync(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ExecutorInvokedEvent e when supported.Contains(e.ExecutorId):
                    await onNode(new GraphNodeEvent(e.ExecutorId, GraphNodePhase.Running, null, null)).ConfigureAwait(false);
                    break;
                case ExecutorCompletedEvent e when supported.Contains(e.ExecutorId):
                    await onNode(new GraphNodeEvent(
                        e.ExecutorId, GraphNodePhase.Done, state.NodeMeta.GetValueOrDefault(e.ExecutorId), null)).ConfigureAwait(false);
                    break;
                case ExecutorFailedEvent e when supported.Contains(e.ExecutorId):
                    failure ??= e.Data?.Message ?? "node failed";
                    await onNode(new GraphNodeEvent(e.ExecutorId, GraphNodePhase.Failed, null, failure)).ConfigureAwait(false);
                    break;
            }
        }

        return new GraphRunResult(failure is null, failure);
    }

    // ---- compile PlanGraph -> MAF Workflow ----

    private Workflow BuildWorkflow(PlanGraph graph, GraphValidationResult plan, HashSet<string> supported, int nMax)
    {
        var byId = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // One MAF executor per supported node. The executor id IS the node id, so the canvas can correlate
        // MAF's ExecutorInvoked/Completed/Failed events back to the node that emitted them.
        var exec = new Dictionary<string, NodeExecutor>(StringComparer.Ordinal);
        foreach (var id in supported)
        {
            var node = byId[id];
            exec[id] = new NodeExecutor(id, RunnerFor(node));
        }

        var start = exec[plan.StartNodeId!];
        var builder = new WorkflowBuilder(start);

        foreach (var node in graph.Nodes.Where(n => supported.Contains(n.Id)))
        {
            var outs = graph.Edges
                .Where(e => e.SourceId == node.Id && exec.ContainsKey(e.TargetId))
                .ToList();
            if (outs.Count == 0)
            {
                continue;
            }

            var isGate = string.Equals(node.StepType, "Evaluator", StringComparison.Ordinal)
                || RoleOf(node) == "Qa";
            if (isGate)
            {
                WireGate(builder, exec, node, outs, nMax);
            }
            else
            {
                foreach (var e in outs)
                {
                    builder.AddEdge(exec[node.Id], exec[e.TargetId]);
                }
            }
        }

        // Output from the End node(s); fall back to the last node in linear order so a graph without an
        // explicit End still produces a terminating output rather than idling.
        var ends = graph.Nodes
            .Where(n => supported.Contains(n.Id) && string.Equals(n.StepType, "End", StringComparison.Ordinal))
            .Select(n => exec[n.Id])
            .ToList();
        if (ends.Count == 0 && plan.LinearOrder.Count > 0)
        {
            var lastId = plan.LinearOrder.LastOrDefault(supported.Contains);
            if (lastId is not null)
            {
                ends.Add(exec[lastId]);
            }
        }
        foreach (var end in ends)
        {
            builder.WithOutputFrom(end);
        }

        return builder.Build();
    }

    // QA / Evaluator gate: a "fail/loop/regenerate"-labelled edge loops back while inconsistent and under the
    // iteration cap; the forward edge(s) fire once consistent OR the cap is exhausted — so exactly one branch
    // is always taken and the workflow terminates.
    private static void WireGate(
        WorkflowBuilder builder, Dictionary<string, NodeExecutor> exec, PlanNode node, List<PlanEdge> outs, int nMax)
    {
        var cap = node.MaxIterations > 0 ? Math.Min(nMax, node.MaxIterations) : nMax;
        var loop = outs.FirstOrDefault(e => IsLoopLabel(e.Label));
        if (loop is null)
        {
            foreach (var e in outs)
            {
                builder.AddEdge(exec[node.Id], exec[e.TargetId]);
            }
            return;
        }

        builder.AddEdge<GraphState>(exec[node.Id], exec[loop.TargetId],
            s => s is not null && !s.LastConsistent && s.Iteration < cap);
        foreach (var e in outs.Where(e => e != loop))
        {
            builder.AddEdge<GraphState>(exec[node.Id], exec[e.TargetId],
                s => s is not null && (s.LastConsistent || s.Iteration >= cap));
        }
    }

    private static bool IsLoopLabel(string label)
        => label.Contains("fail", StringComparison.OrdinalIgnoreCase)
        || label.Contains("loop", StringComparison.OrdinalIgnoreCase)
        || label.Contains("regenerate", StringComparison.OrdinalIgnoreCase);

    // ---- per-node work (the executor body) ----

    private Func<GraphState, CancellationToken, ValueTask<string?>> RunnerFor(PlanNode node)
    {
        if (string.Equals(node.StepType, "Evaluator", StringComparison.Ordinal))
        {
            return (s, ct) => RunQaAsync(s, ct);
        }
        if (string.Equals(node.StepType, "End", StringComparison.Ordinal))
        {
            return (_, _) => new ValueTask<string?>("complete");
        }
        return node.StepType switch
        {
            "Agent" => RoleOf(node) switch
            {
                "Requirement" => (s, ct) => RunRequirementAsync(s, ct),
                "Coding" => (s, ct) => RunCodingAsync(s, ct),
                "Testing" => (s, ct) => RunTestingAsync(s, ct),
                "Qa" => (s, ct) => RunQaAsync(s, ct),
                _ => (_, _) => throw new LlmException($"Agent role '{node.AgentRole}' is not runnable."),
            },
            "Llm" => (s, ct) => RunLlmAsync(node, s, ct),
            "Tool" => (s, ct) => RunToolAsync(node, s, ct),
            "Transform" => (s, _) => new ValueTask<string?>(RunTransform(node, s)),
            "ExtractJson" => (s, _) => new ValueTask<string?>(RunExtractJson(node, s)),
            "Print" => (s, _) => new ValueTask<string?>(ResolveInput(node.Input, s)),
            _ => (_, _) => throw new LlmException($"Node type '{node.StepType}' is not runnable."),
        };
    }

    private async ValueTask<string?> RunRequirementAsync(GraphState s, CancellationToken ct)
    {
        s.Spec = await _requirement.RunAsync(s.Story, ct).ConfigureAwait(false);
        s.Bag["spec"] = s.Spec.Title;
        return Metric(s.Spec.Metrics);
    }

    private async ValueTask<string?> RunCodingAsync(GraphState s, CancellationToken ct)
    {
        s.Iteration++;   // a coding pass starts each iteration of the QA loop
        EnsureSpec(s);
        s.Code = await _coding.RunAsync(s.Spec!, s.LastQa, ct).ConfigureAwait(false);
        s.Bag["code"] = $"{s.Code.Files.Count} files";
        return Metric(s.Code.Metrics);
    }

    private async ValueTask<string?> RunTestingAsync(GraphState s, CancellationToken ct)
    {
        EnsureSpec(s);
        s.Tests = await _testing.RunAsync(s.Spec!, s.Code!, s.LastQa, ct).ConfigureAwait(false);
        s.Bag["tests"] = $"{s.Tests.TotalCount} tests";
        return Metric(s.Tests.Metrics);
    }

    private async ValueTask<string?> RunQaAsync(GraphState s, CancellationToken ct)
    {
        EnsureSpec(s);
        s.LastQa = await _qa.RunAsync(s.Spec!, s.Code!, s.Tests!, ct).ConfigureAwait(false);
        s.Bag["qa"] = s.LastQa.Score.ToString("0.00", CultureInfo.InvariantCulture);
        return $"{Metric(s.LastQa.Metrics)} · consistent={s.LastQa.IsConsistent}";
    }

    private async ValueTask<string?> RunLlmAsync(PlanNode node, GraphState s, CancellationToken ct)
    {
        var prompt = Interpolate(string.IsNullOrWhiteSpace(node.Description) ? node.Title : node.Description, s);
        var opt = _agents.Orchestrator;
        var request = new LlmRequest(string.Empty, prompt, opt.Model, opt.Temperature, opt.MaxTokens);
        request.Validate();
        var response = await _llmFactory.Create(opt.Provider).SendAsync(request, ct).ConfigureAwait(false);
        WriteOut(node, s, response.Content);
        return $"{response.InputTokens}→{response.OutputTokens} tok · ${response.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture)}";
    }

    private async ValueTask<string?> RunToolAsync(PlanNode node, GraphState s, CancellationToken ct)
    {
        var (toolName, input) = ParseToolSpec(node, s);
        var tool = _toolRegistry.Resolve(toolName)
            ?? throw new LlmException($"Tool '{toolName}' is not registered.");

        var request = new ToolInvocationRequest(
            ToolName: toolName,
            CallId: Guid.NewGuid().ToString("N"),
            Input: input,
            TenantId: s.TenantId,
            RunId: s.RunId);
        request.Validate();

        var result = await _toolGateway.InvokeAsync(tool, request, ct).ConfigureAwait(false);
        WriteOut(node, s, result.Output);
        return result.Denied ? "denied by policy" : result.IsError ? "tool error" : "ok";
    }

    private static string RunTransform(PlanNode node, GraphState s)
    {
        var template = string.IsNullOrWhiteSpace(node.Description) ? "${" + node.Input.Trim() + "}" : node.Description;
        var value = Interpolate(template, s);
        WriteOut(node, s, value);
        return Truncate(value, 80);
    }

    private static string RunExtractJson(PlanNode node, GraphState s)
    {
        var json = JsonExtractor.ExtractJson(ResolveInput(node.Input, s), "ExtractJson");
        WriteOut(node, s, json);
        return "json ok";
    }

    // ---- state helpers ----

    private static void WriteOut(PlanNode node, GraphState s, string value)
    {
        var key = string.IsNullOrWhiteSpace(node.Output) ? node.Id : node.Output.Trim();
        s.Bag[key] = value;
    }

    // Resolve "in:" — the first comma-separated key, looked up in the bag, then the well-known artifacts.
    private static string ResolveInput(string inputSpec, GraphState s)
    {
        var key = (inputSpec ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        return Lookup(key, s) ?? inputSpec ?? string.Empty;
    }

    // Replace ${key} placeholders with bag values or well-known artifact summaries.
    private static string Interpolate(string template, GraphState s)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("${", StringComparison.Ordinal))
        {
            return template ?? string.Empty;
        }
        return System.Text.RegularExpressions.Regex.Replace(
            template, @"\$\{([^}]+)\}", m => Lookup(m.Groups[1].Value.Trim(), s) ?? m.Value);
    }

    private static string? Lookup(string key, GraphState s)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }
        if (s.Bag.TryGetValue(key, out var v))
        {
            return v;
        }
        return key.ToLowerInvariant() switch
        {
            "userstory" or "input" or "story" => s.Story.Description,
            "spec" => s.Spec?.Title,
            "code" => s.Code is null ? null : $"{s.Code.Files.Count} files",
            "tests" => s.Tests is null ? null : $"{s.Tests.TotalCount} tests",
            "qa" => s.LastQa?.Score.ToString("0.00", CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    private static (string Tool, string Input) ParseToolSpec(PlanNode node, GraphState s)
    {
        // A Tool node's Description is {"tool":"<name>","input":{...}}; input values may use ${state} refs.
        if (string.IsNullOrWhiteSpace(node.Description))
        {
            throw new LlmException($"Tool node '{node.Title}' has no spec — set Description to {{\"tool\":\"name\",\"input\":{{…}}}}.");
        }
        try
        {
            using var doc = JsonDocument.Parse(node.Description);
            var root = doc.RootElement;
            var name = root.TryGetProperty("tool", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new LlmException($"Tool node '{node.Title}' spec is missing a \"tool\" name.");
            }
            var input = root.TryGetProperty("input", out var i) ? i.GetRawText() : "{}";
            return (name!, Interpolate(input, s));
        }
        catch (JsonException ex)
        {
            throw new LlmException($"Tool node '{node.Title}' spec is not valid JSON: {ex.Message}");
        }
    }

    private static string? RoleOf(PlanNode node)
        => string.Equals(node.StepType, "Evaluator", StringComparison.Ordinal) ? "Qa" : AgentRoleMap.Canonical(node.AgentRole);

    private static void EnsureSpec(GraphState s)
    {
        if (s.Spec is null)
        {
            throw new LlmException("A Coding/Testing/QA node ran before a Requirement node — wire Requirement first.");
        }
    }

    private static string Metric(AgentMetrics m)
        => $"{m.InputTokens}→{m.OutputTokens} tok · ${m.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture)}";

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "…");

    private static string Coalesce(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    // ---- the message threaded along every edge + the MAF executor wrapper ----

    /// <summary>Mutable run state passed node-to-node along the workflow edges (one instance per run).</summary>
    internal sealed class GraphState(string userStory, int nMax, string tenantId, string runId)
    {
        public UserStory Story { get; } = new(userStory, NMax: nMax);
        public string TenantId { get; } = tenantId;
        public string RunId { get; } = runId;
        public int Iteration { get; set; }
        public RequirementSpec? Spec { get; set; }
        public CodeArtifact? Code { get; set; }
        public TestArtifact? Tests { get; set; }
        public QaReport? LastQa { get; set; }
        public bool LastConsistent => LastQa?.IsConsistent ?? false;
        public Dictionary<string, string> Bag { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string?> NodeMeta { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A MAF executor whose body is the node's work delegate. Returns the (mutated) state so MAF
    /// forwards it along the outgoing edges; the terminal executor's return value is harvested as the
    /// workflow output via <c>WithOutputFrom</c>. Stashes the node's display meta for the live canvas.</summary>
    private sealed class NodeExecutor(string id, Func<GraphState, CancellationToken, ValueTask<string?>> run)
        : Executor<GraphState, GraphState>(id)
    {
        public override async ValueTask<GraphState> HandleAsync(GraphState state, IWorkflowContext context, CancellationToken ct)
        {
            state.NodeMeta[Id] = await run(state, ct).ConfigureAwait(false);
            return state;
        }
    }
}
