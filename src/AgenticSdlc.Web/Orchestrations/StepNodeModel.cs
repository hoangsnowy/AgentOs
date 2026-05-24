// AgenticSdlc.Web/Orchestrations/StepNodeModel.cs
// Phase 7b — Custom NodeModel for Z.Blazor.Diagrams: wraps GraphNode + runtime state.
// Each node has 4 ports (Left/Right/Top/Bottom) for free edge connections.

using System;
using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;

namespace AgenticSdlc.Web.Orchestrations;

/// <summary>A node on the canvas — bound to its <see cref="GraphNode"/> data + run state.</summary>
public sealed class StepNodeModel : NodeModel
{
    /// <summary>Initialize from node data; add 4 ports.</summary>
    public StepNodeModel(GraphNode data)
        : base(data.Id, new Point(data.X, data.Y))
    {
        Data = data;
        AddPort(PortAlignment.Left);
        AddPort(PortAlignment.Right);
        AddPort(PortAlignment.Top);
        AddPort(PortAlignment.Bottom);
    }

    /// <summary>The step data.</summary>
    public GraphNode Data { get; }

    /// <summary>Current run state (realtime coloring).</summary>
    public NodeRunState RunState { get; set; } = NodeRunState.Idle;

    /// <summary>Metric line shown under the card while running/done (token/cost/latency).</summary>
    public string? RunMeta { get; set; }

    /// <summary>Callback to open the inspector for this node (set by the page; invoked by the widget on the ✎ button).</summary>
    public Action? RequestEdit { get; set; }
}
