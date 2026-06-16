// The pure, LLM-free graph planner: validation rules + forward execution order. No agents, no Web.

using System.Linq;
using AgentOs.Modules.Pipeline.GraphExecution;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.GraphExecution;

public sealed class GraphPlannerTests
{
    private static PlanNode N(string id, string type, string? role = null, bool start = false, int maxIter = 0)
        => new(id, type, role, id, maxIter, start);

    private static PlanEdge E(string src, string tgt, string label = "")
        => new($"{src}->{tgt}", src, tgt, label);

    // req(Requirement,start) -> cod(Coding) -> tst(Testing) -> qa(Evaluator) -> agg(End); qa loops back to cod on fail.
    private static PlanGraph Sdlc(params PlanNode[] overrideNodes)
    {
        var nodes = overrideNodes.Length > 0 ? overrideNodes :
        [
            N("req", "Agent", "Requirement", start: true),
            N("cod", "Agent", "Coding"),
            N("tst", "Agent", "Testing"),
            N("qa",  "Evaluator", maxIter: 3),
            N("agg", "End"),
        ];
        var edges = new[]
        {
            E("req", "cod"),
            E("cod", "tst"),
            E("tst", "qa"),
            E("qa", "agg", "pass"),
            E("qa", "cod", "fail · regenerate"),
        };
        return new PlanGraph("g1", "SDLC", nodes, edges);
    }

    [Fact]
    public void Plan_SdlcLinearGraph_IsRunnableWithCorrectOrder()
    {
        var result = GraphPlanner.Plan(Sdlc());

        result.IsRunnable.ShouldBeTrue();
        result.StartNodeId.ShouldBe("req");
        result.LinearOrder.ShouldBe(["req", "cod", "tst", "qa", "agg"]);
        result.Nodes.ShouldAllBe(n => n.Support == NodeSupport.Supported);
    }

    [Fact]
    public void Plan_NoStartNode_IsNotRunnableWithError()
    {
        var result = GraphPlanner.Plan(Sdlc(
            N("req", "Agent", "Requirement"),   // start: false
            N("cod", "Agent", "Coding"),
            N("tst", "Agent", "Testing"),
            N("qa", "Evaluator"),
            N("agg", "End")));

        result.IsRunnable.ShouldBeFalse();
        result.StartNodeId.ShouldBeNull();
        result.Errors.ShouldContain(e => e.Contains("start"));
    }

    [Fact]
    public void Plan_UnsupportedNodeOnPath_FlagsTypeAndBlocksRun()
    {
        var graph = new PlanGraph("g", "x",
            [
                N("req", "Agent", "Requirement", start: true),
                N("hook", "Webhook"),   // not a known node type
                N("agg", "End"),
            ],
            [E("req", "hook"), E("hook", "agg")]);

        var result = GraphPlanner.Plan(graph);

        result.IsRunnable.ShouldBeFalse();
        var hook = result.Nodes.Single(n => n.NodeId == "hook");
        hook.Support.ShouldBe(NodeSupport.UnsupportedType);
        hook.Reason.ShouldContain("Webhook");
    }

    [Theory]
    [InlineData("IfElse")]
    [InlineData("Switch")]
    [InlineData("Loop")]
    [InlineData("Parallel")]
    [InlineData("Merge")]
    [InlineData("Human")]
    public void Plan_ControlFlowNode_IsSupported(string type)
    {
        var graph = new PlanGraph("g", "x",
            [N("a", "Llm", start: true), N("flow", type), N("end", "End")],
            [E("a", "flow"), E("flow", "end")]);

        var result = GraphPlanner.Plan(graph);

        result.Nodes.Single(n => n.NodeId == "flow").Support.ShouldBe(NodeSupport.Supported);
        result.IsRunnable.ShouldBeTrue();
    }

    [Fact]
    public void Plan_BranchWithUnsupportedNodeOnSecondBranch_BlocksRun()
    {
        // A Parallel fans out to two branches; the SECOND branch holds an unsupported node. The branch-aware
        // (BFS) reachability must catch it, not just the first edge.
        var graph = new PlanGraph("g", "x",
            [
                N("p", "Parallel", start: true),
                N("ok", "Llm"),
                N("bad", "Webhook"),
                N("end", "End"),
            ],
            [E("p", "ok"), E("p", "bad"), E("ok", "end"), E("bad", "end")]);

        var result = GraphPlanner.Plan(graph);

        result.IsRunnable.ShouldBeFalse();
        result.Nodes.Single(n => n.NodeId == "bad").Support.ShouldBe(NodeSupport.UnsupportedType);
    }

    [Fact]
    public void Plan_AgentNodeUnknownRole_FlagsUnknownRole()
    {
        var result = GraphPlanner.Plan(Sdlc(
            N("req", "Agent", "Requirement", start: true),
            N("cod", "Agent", "Architect"),   // unknown role
            N("agg", "End")));

        result.IsRunnable.ShouldBeFalse();
        var cod = result.Nodes.Single(n => n.NodeId == "cod");
        cod.Support.ShouldBe(NodeSupport.UnknownAgentRole);
        cod.Reason.ShouldContain("Architect");
    }

    [Fact]
    public void Plan_DanglingEdge_IsNotRunnableWithError()
    {
        var graph = new PlanGraph("g", "x",
            [N("req", "Agent", "Requirement", start: true), N("agg", "End")],
            [E("req", "agg"), E("req", "ghost")]);

        var result = GraphPlanner.Plan(graph);

        result.IsRunnable.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Contains("ghost"));
    }

    [Fact]
    public void Plan_QaEvaluatorAndEnd_AreSupportedWithoutAgentRole()
    {
        var graph = new PlanGraph("g", "x",
            [N("qa", "Evaluator", start: true), N("agg", "End")],
            [E("qa", "agg")]);

        var result = GraphPlanner.Plan(graph);

        result.Nodes.ShouldAllBe(n => n.Support == NodeSupport.Supported);
    }
}
