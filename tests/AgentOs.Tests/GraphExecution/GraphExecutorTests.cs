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
    public async Task RunAsync_BudgetExceeded_BlocksBeforeAnyAgentRuns()
    {
        var guard = Substitute.For<AgentOs.Domain.Cost.IBudgetGuard>();
        guard.EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new AgentOs.Domain.Cost.BudgetStatus(
                 CapUsd: 10m, SpentUsd: 25m, RemainingUsd: -15m, Percent: 2.5,
                 State: AgentOs.Domain.Cost.BudgetState.Exceeded, EnforceOn: true));
        var (exec, agents) = Build(qaConsistent: true, budget: guard);
        var graph = new PlanGraph("g", "lin",
            [N("req", "Agent", "Requirement", start: true), N("end", "End")],
            [E("req", "end")]);

        var result = await exec.RunAsync(graph, "build a thing", nMax: 3, "tenant-over-cap", _ => Task.CompletedTask);

        result.Completed.ShouldBeFalse();
        result.FailureMessage!.ShouldContain("Budget exceeded");
        // The gate ran BEFORE any agent — no LLM spend incurred.
        await agents.Requirement.DidNotReceive().RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_BudgetWithinCap_RunsNormally()
    {
        var guard = Substitute.For<AgentOs.Domain.Cost.IBudgetGuard>();
        guard.EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(new AgentOs.Domain.Cost.BudgetStatus(
                 CapUsd: 100m, SpentUsd: 5m, RemainingUsd: 95m, Percent: 0.05,
                 State: AgentOs.Domain.Cost.BudgetState.Ok, EnforceOn: true));
        var (exec, agents) = Build(qaConsistent: true, budget: guard);
        var graph = new PlanGraph("g", "lin",
            [N("req", "Agent", "Requirement", start: true), N("end", "End")],
            [E("req", "end")]);

        var result = await exec.RunAsync(graph, "build a thing", nMax: 3, "tenant-ok", _ => Task.CompletedTask);

        result.Completed.ShouldBeTrue();
        await agents.Requirement.Received(1).RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>());
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
            [N("req", "Agent", "Requirement", start: true), N("hook", "Webhook"), N("end", "End")],
            [E("req", "hook"), E("hook", "end")]);

        var events = new List<GraphNodeEvent>();
        var result = await exec.RunAsync(graph, "s", 3, "t", e => { events.Add(e); return Task.CompletedTask; });

        result.Completed.ShouldBeFalse();
        result.FailureMessage!.ShouldContain("not runnable");
        events.ShouldContain(e => e.NodeId == "hook" && e.Phase == GraphNodePhase.Skipped);
        await agents.Requirement.DidNotReceive().RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ParallelFanOutThenMerge_RunsBothBranchesAndJoins()
    {
        var (exec, _) = Build(llmReply: "ok", yieldLlm: true);   // yield → branches truly run in parallel
        // s -> P (fan-out) -> a, b ; a, b -> M (fan-in barrier) -> end
        var graph = new PlanGraph("g", "par",
            [
                N("s", "Llm", start: true), N("p", "Parallel"),
                N("a", "Llm"), N("b", "Llm"), N("m", "Merge"), N("end", "End"),
            ],
            [E("s", "p"), E("p", "a"), E("p", "b"), E("a", "m"), E("b", "m"), E("m", "end")]);

        var done = new List<string>();
        var running = new List<string>();
        var result = await exec.RunAsync(graph, "story", 3, "t", e =>
        {
            if (e.Phase == GraphNodePhase.Done) { done.Add(e.NodeId); }
            if (e.Phase == GraphNodePhase.Running) { running.Add(e.NodeId); }
            return Task.CompletedTask;
        });

        result.Completed.ShouldBeTrue();
        done.ShouldContain("a");
        done.ShouldContain("b");
        done.ShouldContain("m");
        done.ShouldContain("end");
        // The fan-in collapses both branches into ONE downstream emission — end must run exactly once, not
        // once per branch (regression guard for the merge double-fire).
        running.Count(n => n == "end").ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_IfElse_TakesOnlyTheChosenRoute()
    {
        // The (stubbed) router LLM answers "b" → only the b-branch runs.
        var (exec, _) = Build(llmReply: "b");
        var graph = new PlanGraph("g", "if",
            [N("gate", "IfElse", start: true), N("na", "Llm"), N("nb", "Llm")],
            [E("gate", "na", "a"), E("gate", "nb", "b")]);

        var running = new List<string>();
        var result = await exec.RunAsync(graph, "pick", 3, "t",
            e => { if (e.Phase == GraphNodePhase.Running) { running.Add(e.NodeId); } return Task.CompletedTask; });

        result.Completed.ShouldBeTrue();
        running.ShouldContain("nb");
        running.ShouldNotContain("na");
    }

    [Fact]
    public async Task RunAsync_IfElse_WhitespacePaddedLabel_StillRoutesToChosenBranch()
    {
        // The edge label " b " carries stray whitespace and the router LLM replies "b". The chosen route must
        // still match the padded label (both sides are trimmed). Before the fix the asymmetric trim (reply
        // trimmed, route not) failed to match and silently fired the first/default branch instead.
        var (exec, _) = Build(llmReply: "b");
        var graph = new PlanGraph("g", "if",
            [N("gate", "IfElse", start: true), N("na", "Llm"), N("nb", "Llm")],
            [E("gate", "na", "a"), E("gate", "nb", " b ")]);

        var running = new List<string>();
        var result = await exec.RunAsync(graph, "pick", 3, "t",
            e => { if (e.Phase == GraphNodePhase.Running) { running.Add(e.NodeId); } return Task.CompletedTask; });

        result.Completed.ShouldBeTrue();
        running.ShouldContain("nb");
        running.ShouldNotContain("na");
    }

    [Fact]
    public async Task RunAsync_Loop_RepeatsBodyUntilCapThenExits()
    {
        var (exec, _) = Build(llmReply: "x");
        // s -> L ; L --loop--> B -> L ; L --(cap)--> end. cap = 3 ⇒ body runs twice (passes 1,2), exits on 3.
        var graph = new PlanGraph("g", "loop",
            [N("s", "Llm", start: true), N("l", "Loop", maxIter: 3), N("b", "Llm"), N("end", "End")],
            [E("s", "l"), E("l", "b", "loop"), E("b", "l"), E("l", "end")]);

        var bRuns = 0;
        var result = await exec.RunAsync(graph, "go", 5, "t",
            e => { if (e.NodeId == "b" && e.Phase == GraphNodePhase.Running) { bRuns++; } return Task.CompletedTask; });

        result.Completed.ShouldBeTrue();
        bRuns.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_HumanNode_AutoApprovesWhenNoOperatorAttached()
    {
        var (exec, _) = Build(llmReply: "ok");
        var graph = new PlanGraph("g", "human",
            [N("s", "Llm", start: true), N("h", "Human"), N("end", "End")],
            [E("s", "h"), E("h", "end")]);

        var done = new List<string>();
        var result = await exec.RunAsync(graph, "go", 3, "t",
            e => { if (e.Phase == GraphNodePhase.Done) { done.Add(e.NodeId); } return Task.CompletedTask; });

        result.Completed.ShouldBeTrue();
        done.ShouldContain("h");
        done.ShouldContain("end");
    }

    [Fact]
    public async Task RunAsync_IfElse_SubstringRoute_PicksWholeWordNotPrefix()
    {
        // routes ["no","now"]; the LLM replies "now" — must pick "now", not the earlier substring "no".
        var (exec, _) = Build(llmReply: "now");
        var graph = new PlanGraph("g", "if",
            [N("gate", "IfElse", start: true), N("na", "Llm"), N("nb", "Llm")],
            [E("gate", "na", "no"), E("gate", "nb", "now")]);

        var running = new List<string>();
        var result = await exec.RunAsync(graph, "pick", 3, "t",
            e => { if (e.Phase == GraphNodePhase.Running) { running.Add(e.NodeId); } return Task.CompletedTask; });

        result.Completed.ShouldBeTrue();
        running.ShouldContain("nb");
        running.ShouldNotContain("na");
    }

    [Fact]
    public async Task RunAsync_EvaluatorLoopsBackOverNonCodingPath_StillTerminatesAtCap()
    {
        // The QA gate loops back to Testing (NOT Coding), so the global coding counter never advances. The
        // per-gate counter must still bound the loop — otherwise it spins forever (regression for the
        // shared-Iteration bug). cap=2 ⇒ the gate runs twice then exits.
        var (exec, agents) = Build(qaConsistent: false);
        var graph = new PlanGraph("g", "gate",
            [
                N("req", "Agent", "Requirement", start: true), N("cod", "Agent", "Coding"),
                N("tst", "Agent", "Testing"), N("qa", "Evaluator", maxIter: 2), N("end", "End"),
            ],
            [E("req", "cod"), E("cod", "tst"), E("tst", "qa"), E("qa", "tst", "fail"), E("qa", "end", "pass")]);

        var result = await exec.RunAsync(graph, "s", nMax: 5, "t", _ => Task.CompletedTask);

        result.Completed.ShouldBeTrue();
        await agents.Coding.Received(1).RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>());
        await agents.Testing.Received(2).RunAsync(
            Arg.Any<RequirementSpec>(), Arg.Any<CodeArtifact>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_QaBeforeCoding_FailsWithClearMessage()
    {
        var (exec, _) = Build();
        var graph = new PlanGraph("g", "x",
            [N("req", "Agent", "Requirement", start: true), N("qa", "Evaluator"), N("end", "End")],
            [E("req", "qa"), E("qa", "end")]);

        var result = await exec.RunAsync(graph, "s", 3, "t", _ => Task.CompletedTask);

        result.Completed.ShouldBeFalse();
        result.FailureMessage!.ShouldContain("Coding");
    }

    [Fact]
    public async Task RunAsync_RouterIntoBarrierMerge_SkippedBranch_DoesNotReportFalseSuccess()
    {
        // An If/Else picks ONE branch, but both branches feed a Merge (fan-in barrier needs BOTH). The skipped
        // branch never delivers, so the merge + End never run. The run must NOT report success.
        var (exec, _) = Build(llmReply: "a");
        var graph = new PlanGraph("g", "x",
            [
                N("gate", "IfElse", start: true), N("na", "Llm"), N("nb", "Llm"),
                N("m", "Merge"), N("end", "End"),
            ],
            [E("gate", "na", "a"), E("gate", "nb", "b"), E("na", "m"), E("nb", "m"), E("m", "end")]);

        var result = await exec.RunAsync(graph, "s", 3, "t", _ => Task.CompletedTask);

        result.Completed.ShouldBeFalse();
        result.FailureMessage!.ShouldContain("End");
    }

    [Fact]
    public async Task RunAsync_HumanNode_OperatorRejection_StopsTheRun()
    {
        var (exec, _) = Build(llmReply: "ok");
        var graph = new PlanGraph("g", "human",
            [N("s", "Llm", start: true), N("h", "Human"), N("end", "End")],
            [E("s", "h"), E("h", "end")]);

        var events = new List<GraphNodeEvent>();
        var result = await exec.RunAsync(graph, "go", 3, "t",
            e => { events.Add(e); return Task.CompletedTask; },
            onHuman: _ => Task.FromResult(new GraphHumanReply(false, "not now")));

        result.Completed.ShouldBeFalse();
        events.ShouldContain(e => e.NodeId == "h" && e.Phase == GraphNodePhase.Failed);
        events.ShouldNotContain(e => e.NodeId == "end" && e.Phase == GraphNodePhase.Done);
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

    private static (GraphExecutor Exec, Agents Agents) Build(
        bool qaConsistent = true, string? llmReply = null, bool yieldLlm = false,
        AgentOs.Domain.Cost.IBudgetGuard? budget = null)
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

        // Raw-LLM / decision nodes resolve a client from the factory. When llmReply is given, every SendAsync
        // returns it (lets a router test steer the chosen route); otherwise the factory is bare.
        var factory = Substitute.For<ILlmClientFactory>();
        if (llmReply is not null)
        {
            var llm = Substitute.For<ILlmClient>();
            var resp = new LlmResponse(llmReply, 1, 1, 0m, System.TimeSpan.Zero, "m", "Offline");
            if (yieldLlm)
            {
                // Force a real async suspension on the threadpool so fan-out branches genuinely run in
                // parallel — exercises the concurrent-state-access path (ConcurrentDictionary/interlocked).
                llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
                   .Returns(_ => Task.Run(async () => { await Task.Delay(5); return resp; }));
            }
            else
            {
                llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>()).Returns(resp);
            }
            factory.Create(Arg.Any<string>()).Returns(llm);
        }

        var exec = new GraphExecutor(
            req, coding, testing, qa,
            factory,
            Substitute.For<IToolRegistry>(),
            Substitute.For<IToolGateway>(),
            Options.Create(new AgentsOptions()),
            NullLogger<GraphExecutor>.Instance,
            budget);

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
