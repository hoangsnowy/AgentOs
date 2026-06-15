// Proves the visual graph actually compiles into a Microsoft Agent Framework Workflow and runs on the MAF
// in-process runtime: nodes fire in edge order, MAF's event stream drives per-node Running/Done status, and
// the QA gate's conditional fail-edge loops until the iteration cap. Agents are mocked (no LLM keys).

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
using AgentOs.Domain.Tools;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.GraphExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.GraphExecution;

public sealed class GraphExecutorTests
{
    private static PlanNode N(string id, string type, string? role = null, bool start = false, int maxIter = 0)
        => new(id, type, role, id, maxIter, start);

    private static PlanEdge E(string src, string tgt, string label = "")
        => new($"{src}->{tgt}", src, tgt, label);

    [Fact]
    public async Task RunAsync_LinearRequirementToEnd_CompletesAndStreamsNodeStatus()
    {
        var (exec, agents) = Build();
        var graph = new PlanGraph("g", "lin",
            [N("req", "Agent", "Requirement", start: true), N("end", "End")],
            [E("req", "end")]);

        var events = new List<GraphNodeEvent>();
        var result = await exec.RunAsync(graph, "build a thing", nMax: 3, "tenant-1", e => { events.Add(e); return Task.CompletedTask; });

        result.Completed.ShouldBeTrue();
        result.FailureMessage.ShouldBeNull();
        await agents.Requirement.Received(1).RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>());
        // MAF's ExecutorInvoked/Completed events surfaced the requirement node going Running then Done.
        events.ShouldContain(e => e.NodeId == "req" && e.Phase == GraphNodePhase.Running);
        events.ShouldContain(e => e.NodeId == "req" && e.Phase == GraphNodePhase.Done);
    }

    [Fact]
    public async Task RunAsync_QaPassFirstIteration_RunsCodingOnce()
    {
        var (exec, agents) = Build(qaConsistent: true);

        var result = await exec.RunAsync(Sdlc(), "story", nMax: 3, "t", _ => Task.CompletedTask);

        result.Completed.ShouldBeTrue();
        await agents.Coding.Received(1).RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_QaInconsistent_LoopsCodingUntilIterationCap()
    {
        var (exec, agents) = Build(qaConsistent: false);

        // qa cap = 2 (the Evaluator node's MaxIterations) → coding runs on iterations 1 and 2, then the
        // forward edge fires because the cap is exhausted, and the workflow terminates at End.
        var result = await exec.RunAsync(Sdlc(qaMaxIter: 2), "story", nMax: 5, "t", _ => Task.CompletedTask);

        result.Completed.ShouldBeTrue();
        await agents.Coding.Received(2).RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_UnsupportedNodeOnPath_RefusesBeforeAnyAgentRuns()
    {
        var (exec, agents) = Build();
        var graph = new PlanGraph("g", "x",
            [N("req", "Agent", "Requirement", start: true), N("par", "Parallel"), N("end", "End")],
            [E("req", "par"), E("par", "end")]);

        var events = new List<GraphNodeEvent>();
        var result = await exec.RunAsync(graph, "s", 3, "t", e => { events.Add(e); return Task.CompletedTask; });

        result.Completed.ShouldBeFalse();
        result.FailureMessage!.ShouldContain("not runnable");
        events.ShouldContain(e => e.NodeId == "par" && e.Phase == GraphNodePhase.Skipped);
        await agents.Requirement.DidNotReceive().RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>());
    }

    // req(Requirement,start) -> cod(Coding) -> tst(Testing) -> qa(Evaluator) -> end; qa loops to cod on fail.
    private static PlanGraph Sdlc(int qaMaxIter = 3)
        => new("g1", "SDLC",
            [
                N("req", "Agent", "Requirement", start: true),
                N("cod", "Agent", "Coding"),
                N("tst", "Agent", "Testing"),
                N("qa", "Evaluator", maxIter: qaMaxIter),
                N("end", "End"),
            ],
            [
                E("req", "cod"),
                E("cod", "tst"),
                E("tst", "qa"),
                E("qa", "end", "pass"),
                E("qa", "cod", "fail · regenerate"),
            ]);

    private sealed record Agents(IRequirementAgent Requirement, ICodingAgent Coding, ITestingAgent Testing, IQaAgent Qa);

    private static (GraphExecutor Exec, Agents Agents) Build(bool qaConsistent = true)
    {
        var req = Substitute.For<IRequirementAgent>();
        req.RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>()).Returns(StubSpec());

        var coding = Substitute.For<ICodingAgent>();
        coding.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>()).Returns(StubCode());

        var testing = Substitute.For<ITestingAgent>();
        testing.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<CodeArtifact>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>())
               .Returns(StubTests());

        var qa = Substitute.For<IQaAgent>();
        qa.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<CodeArtifact>(), Arg.Any<TestArtifact>(), Arg.Any<CancellationToken>())
          .Returns(StubQa(qaConsistent));

        var exec = new GraphExecutor(
            req, coding, testing, qa,
            Substitute.For<ILlmClientFactory>(),
            Substitute.For<IToolRegistry>(),
            Substitute.For<IToolGateway>(),
            Options.Create(new AgentsOptions()),
            NullLogger<GraphExecutor>.Instance);

        return (exec, new Agents(req, coding, testing, qa));
    }

    private static RequirementSpec StubSpec() => new(
        Title: "T", Summary: "S", Stakeholders: [], FunctionalRequirements: [], NonFunctionalRequirements: [],
        Entities: [new EntityDescriptor("E", [])], Endpoints: [new EndpointDescriptor("GET", "/", "root")],
        AcceptanceCriteria: ["a"], Metrics: new AgentMetrics("Test", "m", 10, 5, 0.0001m, System.TimeSpan.FromMilliseconds(50)));

    private static CodeArtifact StubCode() => new(
        ProjectName: "P", Architecture: "Clean Architecture",
        Files: [new CodeFile("src/E.cs", "namespace P;")], Notes: null,
        Metrics: new AgentMetrics("Test", "m", 20, 10, 0.0002m, System.TimeSpan.FromMilliseconds(80)));

    private static TestArtifact StubTests() => new(
        Framework: "xUnit", Files: [new CodeFile("tests/ETests.cs", "namespace T;")],
        HappyPathCount: 1, EdgeCaseCount: 1, ErrorCaseCount: 1, EstimatedCoveragePercent: 60,
        Metrics: new AgentMetrics("Test", "m", 15, 8, 0.00015m, System.TimeSpan.FromMilliseconds(70)));

    private static QaReport StubQa(bool isConsistent) => new(
        Score: isConsistent ? 0.9 : 0.6, IsConsistent: isConsistent, IterationNeeded: !isConsistent,
        Issues: isConsistent ? [] : [new QaIssue("Major", "TestCoverage", "missing edge")],
        Recommendations: isConsistent ? [] : ["add edge test"],
        Metrics: new AgentMetrics("Test", "m", 12, 6, 0.0001m, System.TimeSpan.FromMilliseconds(40)));
}
