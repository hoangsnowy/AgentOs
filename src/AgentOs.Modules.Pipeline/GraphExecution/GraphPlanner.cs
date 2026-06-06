// Pure, LLM-free graph validation + execution-order planning. The unit-test target. Given a PlanGraph it
// classifies every node, finds graph-level blockers, and computes the forward execution order from Start.
// A graph is runnable iff: exactly one Start, no dangling edges, and every node on the forward path is
// Supported (a known-role Agent, the QA Evaluator gate, or End).

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

        // Forward execution order from Start: follow edges to not-yet-visited nodes (back-edges ignored).
        var order = new List<string>();
        if (start is not null)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var cursor = start;
            var guard = 0;
            while (cursor is not null && guard++ <= graph.Nodes.Count)
            {
                if (!visited.Add(cursor.Id))
                {
                    break; // cycle guard — order is best-effort
                }
                order.Add(cursor.Id);

                var nextId = graph.Edges
                    .Where(e => e.SourceId == cursor.Id)
                    .Select(e => e.TargetId)
                    .FirstOrDefault(t => byId.ContainsKey(t) && !visited.Contains(t));
                cursor = nextId is null ? null : byId[nextId];
            }
        }

        // Runnable iff no graph errors, a single start, and no unsupported node ON the forward path.
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
            case "End":
                return new NodeValidation(n.Id, NodeSupport.Supported, "end");
            default:
                return new NodeValidation(n.Id, NodeSupport.UnsupportedType, $"Node type '{n.StepType}' is not supported yet.");
        }
    }
}
