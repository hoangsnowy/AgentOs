// Pure, LLM-free graph validation + execution-order planning. The unit-test target. Given a PlanGraph it
// classifies every node, finds graph-level blockers, and computes the forward execution order from Start.
// A graph is runnable iff: exactly one Start, no dangling edges, and every node REACHABLE from Start is
// Supported. Supported node types: known-role Agent (Requirement/Coding/Testing/Qa), the QA Evaluator gate,
// Llm, Tool, Transform, ExtractJson, Print, End, plus the control-flow set IfElse/Switch/Loop (conditional
// routing), Parallel (fan-out) / Merge (fan-in barrier), and Human (operator checkpoint).

using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentOs.Modules.Pipeline.GraphExecution;

/// <summary>Validates a graph and plans its linear execution order — no LLM, no DI, no async.</summary>
public static class GraphPlanner
{
    public static GraphValidationResult Plan(PlanGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var errors = new List<string>();
        var byId = new Dictionary<string, PlanNode>(StringComparer.Ordinal);
        foreach (var n in graph.Nodes)
        {
            byId[n.Id] = n;
        }

        // Exactly one Start.
        var starts = graph.Nodes.Where(n => n.IsStart).ToList();
        if (starts.Count == 0)
        {
            errors.Add("No start node (set one node's IsStart = true).");
        }
        else if (starts.Count > 1)
        {
            errors.Add($"Multiple start nodes ({string.Join(", ", starts.Select(s => s.Id))}). Exactly one is required.");
        }
        var start = starts.Count == 1 ? starts[0] : null;

        // No dangling edges.
        foreach (var e in graph.Edges)
        {
            if (!byId.ContainsKey(e.SourceId))
            {
                errors.Add($"Edge {e.Id}: source '{e.SourceId}' is not a node.");
            }
            if (!byId.ContainsKey(e.TargetId))
            {
                errors.Add($"Edge {e.Id}: target '{e.TargetId}' is not a node.");
            }
        }

        // Per-node support verdicts (always returned, so the canvas can paint Skipped chips).
        var verdicts = graph.Nodes.Select(Classify).ToList();
        var verdictById = verdicts.ToDictionary(v => v.NodeId, StringComparer.Ordinal);

        // Reachability from Start, following EVERY out-edge (branch-aware, not just the first) so a Parallel
        // fan-out or an If/Else branch is fully covered. Back-edges to already-visited nodes are skipped
        // (cycle-safe). `order` is a best-effort BFS ordering used for output-node fallback.
        var order = new List<string>();
        if (start is not null)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(start.Id);
            visited.Add(start.Id);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                order.Add(id);
                foreach (var e in graph.Edges.Where(e => e.SourceId == id))
                {
                    if (byId.ContainsKey(e.TargetId) && visited.Add(e.TargetId))
                    {
                        queue.Enqueue(e.TargetId);
                    }
                }
            }
        }

        // Runnable iff no graph errors, a single start, and no unsupported node REACHABLE from Start.
        var unsupportedOnPath = order.Any(id => verdictById.TryGetValue(id, out var v) && v.Support != NodeSupport.Supported);
        var isRunnable = errors.Count == 0 && starts.Count == 1 && !unsupportedOnPath;

        return new GraphValidationResult(isRunnable, start?.Id, verdicts, errors, order);
    }

    private static NodeValidation Classify(PlanNode n)
    {
        switch (n.StepType?.Trim())
        {
            case "Agent":
                return AgentRoleMap.IsKnown(n.AgentRole)
                    ? new NodeValidation(n.Id, NodeSupport.Supported, "agent")
                    : new NodeValidation(n.Id, NodeSupport.UnknownAgentRole,
                        $"Agent role '{n.AgentRole}' is not one of Requirement/Coding/Testing/Qa.");
            case "Evaluator":
                return new NodeValidation(n.Id, NodeSupport.Supported, "QA gate");
            // Data / leaf nodes the MAF executors run directly (no control-flow fan-out needed).
            case "Llm":
                return new NodeValidation(n.Id, NodeSupport.Supported, "raw LLM");
            case "Tool":
                return new NodeValidation(n.Id, NodeSupport.Supported, "tool (gated)");
            case "Transform":
                return new NodeValidation(n.Id, NodeSupport.Supported, "transform");
            case "ExtractJson":
                return new NodeValidation(n.Id, NodeSupport.Supported, "extract JSON");
            case "Print":
                return new NodeValidation(n.Id, NodeSupport.Supported, "print");
            case "End":
                return new NodeValidation(n.Id, NodeSupport.Supported, "end");
            // Control-flow: routing (If/Else, Switch, Loop) compiles to MAF conditional edges; Parallel/Merge
            // compile to MAF fan-out / fan-in-barrier edges; Human pauses the run for an operator decision.
            case "IfElse":
                return new NodeValidation(n.Id, NodeSupport.Supported, "if/else branch");
            case "Switch":
                return new NodeValidation(n.Id, NodeSupport.Supported, "switch branch");
            case "Loop":
                return new NodeValidation(n.Id, NodeSupport.Supported, "loop");
            case "Parallel":
                return new NodeValidation(n.Id, NodeSupport.Supported, "parallel fan-out");
            case "Merge":
                return new NodeValidation(n.Id, NodeSupport.Supported, "merge fan-in");
            case "Human":
                return new NodeValidation(n.Id, NodeSupport.Supported, "human checkpoint");
            default:
                return new NodeValidation(n.Id, NodeSupport.UnsupportedType, $"Node type '{n.StepType}' is not supported.");
        }
    }
}
