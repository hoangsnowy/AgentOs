// End-to-end proof for the PR #98 follow-up: a Workflow-studio (graph) run persists its OWN per-node LLM
// spend to run_metrics, stamped with the EXPLICIT tenant (a Blazor circuit has a blank ITenantContext) — so
// the same pre-run IBudgetGuard gate that protects the pipeline path also caps a workflow-only tenant. Uses
// the real BudgetGuard + a real PipelineRunRepository over EF Core InMemory (the circuit-safe read path),
// exactly like CostSummaryTests, with the four typed agents stubbed (no LLM keys).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Cost;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Domain.Tools;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Cost;
using AgentOs.Modules.Pipeline.GraphExecution;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Persistence.Repositories;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.GraphExecution;

public sealed class GraphExecutorBudgetMeteringTests
{
    // A non-blank "circuit" tenant context — deliberately NOT the tenant the run is for, to prove the run's
    // rows are stamped from the explicit RunAsync tenant rather than this ambient one.
    private sealed class FixedTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
        public string? UserId => "u";
        public string? UserName => "u";
        public IReadOnlyList<string> Roles { get; } = ["member"];
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    private const string CircuitBlankTenant = "circuit-no-tenant";
    private const string RunTenant = "acme";

    private static DbContextOptions<PipelineDbContext> NewOptions() =>
        new DbContextOptionsBuilder<PipelineDbContext>()
            .UseInMemoryDatabase($"graph-budget-{Guid.NewGuid()}")
            .Options;

    // Linear req -> cod -> tst -> qa -> end; every agent node bills $0.30 → $1.20 per run.
    private static PlanGraph Sdlc()
        => new("g", "sdlc",
            [
                new PlanNode("req", "Agent", "Requirement", "req", 0, true),
                new PlanNode("cod", "Agent", "Coding", "cod", 0, false),
                new PlanNode("tst", "Agent", "Testing", "tst", 0, false),
                new PlanNode("qa", "Agent", "Qa", "qa", 0, false),
                new PlanNode("end", "End", null, "end", 0, false),
            ],
            [
                new PlanEdge("e1", "req", "cod", ""),
                new PlanEdge("e2", "cod", "tst", ""),
                new PlanEdge("e3", "tst", "qa", ""),
                new PlanEdge("e4", "qa", "end", ""),
            ]);

    private static GraphExecutor BuildExecutor(IBudgetGuard guard, IPipelineRunRepository repo, Action<string?>? onReqAmbientTenant = null)
    {
        var m = new AgentMetrics("Claude", "claude-sonnet-4", 100, 50, 0.30m, TimeSpan.FromMilliseconds(50));

        var reqSpec = new RequirementSpec("T", "S", [], [], [], [new EntityDescriptor("E", [])],
            [new EndpointDescriptor("GET", "/", "root")], ["a"], m);
        var req = Substitute.For<IRequirementAgent>();
        // Capture the ambient tenant in force WHILE the node executes — the same value EfAppConfigStore
        // resolves for the per-tenant LLM-key lookup. The real agent would read its key here.
        req.RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>())
           .Returns(_ => { onReqAmbientTenant?.Invoke(AmbientIdentity.Current?.TenantId); return reqSpec; });

        var coding = Substitute.For<ICodingAgent>();
        coding.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>())
              .Returns(new CodeArtifact("P", "Clean Architecture", [new CodeFile("src/E.cs", "namespace P;")], null, m));

        var testing = Substitute.For<ITestingAgent>();
        testing.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<CodeArtifact>(), Arg.Any<QaReport?>(), Arg.Any<CancellationToken>())
               .Returns(new TestArtifact("xUnit", [new CodeFile("tests/ETests.cs", "namespace T;")], 1, 1, 1, 60, m));

        var qa = Substitute.For<IQaAgent>();
        qa.RunAsync(Arg.Any<RequirementSpec>(), Arg.Any<CodeArtifact>(), Arg.Any<TestArtifact>(), Arg.Any<CancellationToken>())
          .Returns(new QaReport(0.95, true, false, [], [], m));

        return new GraphExecutor(
            req, coding, testing, qa,
            Substitute.For<ILlmClientFactory>(),
            Substitute.For<IToolRegistry>(),
            Substitute.For<IToolGateway>(),
            Options.Create(new AgentsOptions()),
            NullLogger<GraphExecutor>.Instance,
            guard,
            repo,
            TimeProvider.System);
    }

    [Fact]
    public async Task RunAsync_WorkflowSpend_IsMeteredAndTripsBudgetGateOnNextRun()
    {
        var options = NewOptions();
        await using var db = new PipelineDbContext(options, new FixedTenant(CircuitBlankTenant));
        var repo = new PipelineRunRepository(db, new FixedTenant(CircuitBlankTenant));

        var config = new InMemoryAppConfigStore();
        await config.SetForTenantAsync(RunTenant, BudgetGuard.CapKey, "1.00");
        await config.SetForTenantAsync(RunTenant, BudgetGuard.EnforceKey, "true");
        var guard = new BudgetGuard(config, repo, TimeProvider.System);

        var exec = BuildExecutor(guard, repo);

        // First run: spend so far is $0 (< $1.00 cap), so the pre-run gate lets it through; the run then
        // persists its own $1.20 of spend.
        var run1 = await exec.RunAsync(Sdlc(), "story", nMax: 3, RunTenant, _ => Task.CompletedTask);
        run1.Completed.ShouldBeTrue();

        // That persisted spend now makes the tenant over its enforced cap...
        var status = await guard.EvaluateAsync(RunTenant);
        status.State.ShouldBe(BudgetState.Exceeded);
        status.SpentUsd.ShouldBe(1.20m);

        // ...so the SECOND run is blocked by the very gate this fix makes effective.
        var run2 = await exec.RunAsync(Sdlc(), "story", nMax: 3, RunTenant, _ => Task.CompletedTask);
        run2.Completed.ShouldBeFalse();
        run2.FailureMessage!.ShouldContain("Budget exceeded");
    }

    [Fact]
    public async Task RunAsync_PersistsSpend_UnderRunTenant_NotAmbientCircuitTenant()
    {
        var options = NewOptions();
        await using var db = new PipelineDbContext(options, new FixedTenant(CircuitBlankTenant));
        var repo = new PipelineRunRepository(db, new FixedTenant(CircuitBlankTenant));
        var guard = new BudgetGuard(new InMemoryAppConfigStore(), repo, TimeProvider.System); // no cap → never blocks

        var exec = BuildExecutor(guard, repo);
        await exec.RunAsync(Sdlc(), "story", nMax: 3, RunTenant, _ => Task.CompletedTask);

        // The run_metrics rows are billed to the explicit run tenant (4 agent calls, $1.20)...
        var billed = await repo.GetCostSummaryForTenantAsync(RunTenant);
        billed.CallCount.ShouldBe(4);
        billed.TotalCostUsd.ShouldBe(1.20m);

        // ...and NOT to the blank ambient ITenantContext the circuit's DbContext carries.
        var ambient = await repo.GetCostSummaryForTenantAsync(CircuitBlankTenant);
        ambient.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_AgentWork_RunsUnderExplicitRunTenant_SoPerTenantKeyResolves()
    {
        var options = NewOptions();
        await using var db = new PipelineDbContext(options, new FixedTenant(CircuitBlankTenant));
        var repo = new PipelineRunRepository(db, new FixedTenant(CircuitBlankTenant));
        var guard = new BudgetGuard(new InMemoryAppConfigStore(), repo, TimeProvider.System); // no cap → never blocks

        string? ambientDuringRun = null;
        var exec = BuildExecutor(guard, repo, t => ambientDuringRun = t);

        await exec.RunAsync(Sdlc(), "story", nMax: 3, RunTenant, _ => Task.CompletedTask);

        // While a node runs, the ambient identity carries the EXPLICIT run tenant, so the LLM-key lookup
        // (EfAppConfigStore.ResolveTenant reads AmbientIdentity FIRST) resolves THIS tenant's encrypted key
        // — not `default`/the platform appsettings key. Before the GraphExecutor push this was null on a
        // Blazor circuit (no HttpContext → ITenantContext blank), the bug that ran workflows on the wrong key.
        ambientDuringRun.ShouldBe(RunTenant);
    }
}
