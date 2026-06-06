// Runs a validated graph against the REAL typed agents (the same Requirement/Coding/Testing/QA the fixed
// pipeline uses), threading artifacts node-to-node and honouring the QA loop edge. Emits node-id-correlated
// status so the canvas lights up live. Refuses to start a graph that isn't runnable (unsupported nodes on
// the path) — no node ever runs through a generic fallback (the toy executor's bug).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Pipeline.Agents;

namespace AgentOs.Modules.Pipeline.GraphExecution;

/// <summary>Executes a <see cref="PlanGraph"/> via the typed agents. Scoped; ctor is side-effect-free.</summary>
public sealed class GraphExecutor
{
    private const int HardStepCap = 64;

    private readonly IRequirementAgent _requirement;
    private readonly ICodingAgent _coding;
    private readonly ITestingAgent _testing;
    private readonly IQaAgent _qa;

    public GraphExecutor(IRequirementAgent requirement, ICodingAgent coding, ITestingAgent testing, IQaAgent qa)
    {
        _requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
        _coding = coding ?? throw new ArgumentNullException(nameof(coding));
        _testing = testing ?? throw new ArgumentNullException(nameof(testing));
        _qa = qa ?? throw new ArgumentNullException(nameof(qa));
    }

    /// <summary>Validate then run the graph, pushing per-node status to <paramref name="onNode"/>.</summary>
    public async Task<GraphRunResult> RunAsync(
        PlanGraph graph, string userStoryText, int nMax, Func<GraphNodeEvent, Task> onNode, CancellationToken ct = default)
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

        var byId = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var story = new UserStory(userStoryText, NMax: Math.Clamp(nMax, 1, 10));

        RequirementSpec? spec = null;
        CodeArtifact? code = null;
        TestArtifact? tests = null;
        QaReport? lastQa = null;
        var iter = 0;

        var cursor = byId[plan.StartNodeId!];
        var guard = 0;
        while (cursor is not null && guard++ < HardStepCap)
        {
            ct.ThrowIfCancellationRequested();
            await onNode(new GraphNodeEvent(cursor.Id, GraphNodePhase.Running, null, null)).ConfigureAwait(false);

            try
            {
                if (string.Equals(cursor.StepType, "End", StringComparison.Ordinal))
                {
                    await onNode(new GraphNodeEvent(cursor.Id, GraphNodePhase.Done, null, "complete")).ConfigureAwait(false);
                    return new GraphRunResult(true, null);
                }

                var role = RoleOf(cursor);
                string? meta = null;
                switch (role)
                {
                    case "Requirement":
                        spec = await _requirement.RunAsync(story, ct).ConfigureAwait(false);
                        meta = Metric(spec.Metrics);
                        break;
                    case "Coding":
                        EnsureSpec(spec);
                        code = await _coding.RunAsync(spec!, lastQa, ct).ConfigureAwait(false);
                        meta = Metric(code.Metrics);
                        break;
                    case "Testing":
                        EnsureSpec(spec);
                        tests = await _testing.RunAsync(spec!, code!, lastQa, ct).ConfigureAwait(false);
                        meta = Metric(tests.Metrics);
                        break;
                    case "Qa":
                        EnsureSpec(spec);
                        iter++;
                        lastQa = await _qa.RunAsync(spec!, code!, tests!, ct).ConfigureAwait(false);
                        meta = $"{Metric(lastQa.Metrics)} · consistent={lastQa.IsConsistent}";
                        break;
                }

                await onNode(new GraphNodeEvent(cursor.Id, GraphNodePhase.Done, meta, null)).ConfigureAwait(false);
            }
            catch (LlmException ex)
            {
                await onNode(new GraphNodeEvent(cursor.Id, GraphNodePhase.Failed, null, ex.Message)).ConfigureAwait(false);
                return new GraphRunResult(false, ex.Message);
            }

            cursor = ChooseNext(graph, byId, cursor, lastQa, iter, nMax);
        }

        return new GraphRunResult(true, null);
    }

    private static string? RoleOf(PlanNode node)
        => string.Equals(node.StepType, "Evaluator", StringComparison.Ordinal) ? "Qa" : AgentRoleMap.Canonical(node.AgentRole);

    // Honour the QA gate's loop edge; otherwise take the forward (non-"fail") edge.
    private static PlanNode? ChooseNext(
        PlanGraph graph, Dictionary<string, PlanNode> byId, PlanNode node, QaReport? lastQa, int iter, int nMax)
    {
        var outs = graph.Edges.Where(e => e.SourceId == node.Id).ToList();
        if (outs.Count == 0)
        {
            return null;
        }

        PlanNode? Target(PlanEdge e) => byId.TryGetValue(e.TargetId, out var t) ? t : null;
        static bool IsFailLabel(string label)
            => label.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || label.Contains("loop", StringComparison.OrdinalIgnoreCase)
            || label.Contains("regenerate", StringComparison.OrdinalIgnoreCase);

        var isGate = string.Equals(node.StepType, "Evaluator", StringComparison.Ordinal) || RoleOf(node) == "Qa";
        if (isGate && lastQa is { IsConsistent: false })
        {
            var cap = node.MaxIterations > 0 ? Math.Min(nMax, node.MaxIterations) : nMax;
            if (iter < cap)
            {
                var loop = outs.FirstOrDefault(e => IsFailLabel(e.Label));
                if (loop is not null)
                {
                    return Target(loop);
                }
            }
        }

        // Forward: a non-"fail" edge, preferring a "pass"-labelled one.
        var pass = outs.FirstOrDefault(e => e.Label.Contains("pass", StringComparison.OrdinalIgnoreCase))
            ?? outs.FirstOrDefault(e => !IsFailLabel(e.Label))
            ?? outs[0];
        return Target(pass);
    }

    private static void EnsureSpec(RequirementSpec? spec)
    {
        if (spec is null)
        {
            throw new LlmException("Graph ran a Coding/Testing/QA node before a Requirement node — wire Requirement first.");
        }
    }

    private static string Metric(AgentMetrics m)
        => $"{m.InputTokens}→{m.OutputTokens} tok · ${m.CostUsd:0.0000}";
}
