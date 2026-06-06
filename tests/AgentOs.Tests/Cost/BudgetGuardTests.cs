// AgentOs.Tests/Cost/BudgetGuardTests.cs
// The per-tenant budget gate: cap unset -> unconstrained (the standalone no-regression case); spend
// thresholds map to Ok/Warn/Exceeded; and the orchestrator hard-blocks an over-cap enforced run while
// letting a Warn run through.

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Cost;
using AgentOs.Domain.Pipeline;
using AgentOs.Modules.AppConfig;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Cost;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Orchestration;
using AgentOs.Modules.Pipeline.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Cost;

public sealed class BudgetGuardTests
{
    private static async Task<IAppConfigStore> ConfigAsync(string? cap, string? enforce = null)
    {
        // The production in-memory store is a clean test double — no NSubstitute + ValueTask friction.
        var config = new InMemoryAppConfigStore();
        if (cap is not null)
        {
            await config.SetForTenantAsync("t1", BudgetGuard.CapKey, cap);
        }
        if (enforce is not null)
        {
            await config.SetForTenantAsync("t1", BudgetGuard.EnforceKey, enforce);
        }
        return config;
    }

    private static IPipelineRunRepository Runs(decimal spentUsd)
    {
        var runs = Substitute.For<IPipelineRunRepository>();
        runs.GetCostSummaryForTenantAsync("t1", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new CostSummary(spentUsd, 0, 0, 0, 0, [], [], [], []));
        return runs;
    }

    [Fact]
    public async Task Evaluate_CapUnset_ReturnsUnset_AllowsRun()
    {
        var guard = new BudgetGuard(await ConfigAsync(cap: null), Runs(999m), TimeProvider.System);

        var status = await guard.EvaluateAsync("t1");

        status.ShouldBe(BudgetStatus.Unset);
        status.State.ShouldBe(BudgetState.Ok);
        status.EnforceOn.ShouldBeFalse();
    }

    [Fact]
    public async Task Evaluate_SpendOver80Percent_ReturnsWarn()
    {
        var guard = new BudgetGuard(await ConfigAsync(cap: "100"), Runs(85m), TimeProvider.System);

        var status = await guard.EvaluateAsync("t1");

        status.State.ShouldBe(BudgetState.Warn);
        status.Percent.ShouldBe(0.85, 0.0001);
        status.RemainingUsd.ShouldBe(15m);
    }

    [Fact]
    public async Task Evaluate_SpendOverCap_EnforceOn_StateExceeded()
    {
        var guard = new BudgetGuard(await ConfigAsync(cap: "50", enforce: "true"), Runs(60m), TimeProvider.System);

        var status = await guard.EvaluateAsync("t1");

        status.State.ShouldBe(BudgetState.Exceeded);
        status.EnforceOn.ShouldBeTrue();
        status.SpentUsd.ShouldBe(60m);
    }

    [Fact]
    public async Task PersistingOrchestrator_Exceeded_EnforceOn_Throws()
    {
        var guard = Substitute.For<IBudgetGuard>();
        guard.EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetStatus(50m, 60m, -10m, 1.2, BudgetState.Exceeded, EnforceOn: true));
        var inner = Substitute.For<IOrchestratorAgent>();
        var orchestrator = new PersistingOrchestratorAgent(
            inner, Substitute.For<IPipelineRunRepository>(), Substitute.For<IMetricsCollector>(),
            guard, TimeProvider.System, NullLogger<PersistingOrchestratorAgent>.Instance);

        await Should.ThrowAsync<BudgetExceededException>(() => orchestrator.RunAsync(new UserStory("story")));
        await inner.DidNotReceive().RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistingOrchestrator_Warn_DoesNotThrow_InnerRuns()
    {
        var guard = Substitute.For<IBudgetGuard>();
        guard.EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetStatus(100m, 85m, 15m, 0.85, BudgetState.Warn, EnforceOn: true));
        var inner = Substitute.For<IOrchestratorAgent>();
        var metrics = Substitute.For<IMetricsCollector>();
        metrics.Snapshot().Returns(Array.Empty<RunMetric>());
        var orchestrator = new PersistingOrchestratorAgent(
            inner, Substitute.For<IPipelineRunRepository>(), metrics,
            guard, TimeProvider.System, NullLogger<PersistingOrchestratorAgent>.Instance);

        await orchestrator.RunAsync(new UserStory("story"));

        await inner.Received(1).RunAsync(Arg.Any<UserStory>(), Arg.Any<CancellationToken>());
    }
}
