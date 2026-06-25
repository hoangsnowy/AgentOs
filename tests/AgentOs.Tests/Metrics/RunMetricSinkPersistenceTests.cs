// Regression for the metrics-eviction undercount: PersistingOrchestratorAgent must persist a run's FULL
// metric set even under cross-tenant concurrency that floods the shared, bounded InMemoryMetricsCollector.
//
// The old code read a run's rows back out of that process-wide singleton
// (Snapshot().Where(RunId == runKey)); the collector is bounded (MaxRecords, drop-oldest), so under
// concurrency/volume a run's own rows could be evicted before PersistAsync read them → persisted
// run_metrics undercount tokens/cost → BudgetGuard (which sums run_metrics for month-to-date spend)
// under-meters and a tenant runs past its true cap.
//
// The fix routes each run's metrics into a run-owned sink (MetricsContext.RunSink) that the orchestrator
// persists directly — immune to the shared collector's eviction — mirroring GraphExecutor's per-run
// GraphState.Spend bag. This test floods the shared singleton hard enough to evict every run's own rows
// from it, then asserts each run still persisted its complete set.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Cost;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Orchestration;
using AgentOs.Modules.Pipeline.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Metrics;

public sealed class RunMetricSinkPersistenceTests
{
    [Fact]
    public async Task ConcurrentRuns_EachPersistsFullMetricSet_DespiteSharedCollectorEviction()
    {
        const int runCount = 6;
        const int metricsPerRun = 5;

        // One process-wide bounded singleton shared by every run — exactly how DI wires it.
        var shared = new InMemoryMetricsCollector();

        // Capture what each run actually persisted (PipelineRunRecord.Metrics), keyed by RunId.
        var persisted = new ConcurrentDictionary<Guid, IReadOnlyList<RunMetric>>();
        var repo = Substitute.For<IPipelineRunRepository>();
        repo.When(r => r.SaveAsync(Arg.Any<PipelineRunRecord>(), Arg.Any<CancellationToken>()))
            .Do(ci =>
            {
                var record = ci.Arg<PipelineRunRecord>();
                persisted[record.Id] = record.Metrics;
            });

        // Budget gate open so the run proceeds; tenant blank (the standalone/no-DB shape).
        var guard = Substitute.For<IBudgetGuard>();
        guard.EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(BudgetStatus.Unset);
        var tenant = Substitute.For<AgentOs.SharedKernel.Identity.ITenantContext>();
        tenant.TenantId.Returns("default");

        async Task RunOneAsync()
        {
            var inner = new MetricEmittingInner(shared, metricsPerRun, floodNoise: InMemoryMetricsCollector.MaxRecords);
            var orchestrator = new PersistingOrchestratorAgent(
                inner, repo, guard, TimeProvider.System, tenant, NullLogger<PersistingOrchestratorAgent>.Instance);
            await orchestrator.RunAsync(new UserStory("story"));
        }

        await Task.WhenAll(Enumerable.Range(0, runCount).Select(_ => RunOneAsync()));

        // Every run persisted, and each persisted EXACTLY its own rows — none lost to drop-oldest eviction.
        persisted.Count.ShouldBe(runCount);
        foreach (var (runId, metrics) in persisted)
        {
            metrics.Count.ShouldBe(metricsPerRun);
            metrics.ShouldAllBe(m => m.RunId == runId.ToString());
        }

        // Meanwhile the shared singleton was flooded past its cap and evicted down to it — proving the
        // pressure was real. (Lossy there is fine; it is only the live cost-dashboard working set.)
        shared.Snapshot().Count.ShouldBe(InMemoryMetricsCollector.MaxRecords);
    }

    // Stand-in for the inner pipeline. Emits this run's metrics the way LlmAgentBase does — to the shared
    // collector (live, lossy) AND the ambient run sink (durable) — then floods the shared collector past its
    // cap so the run's own rows are evicted from it: the exact condition the per-run sink must survive.
    private sealed class MetricEmittingInner(IMetricsCollector shared, int metricsPerRun, int floodNoise)
        : IOrchestratorAgent
    {
        public async Task<PipelineResult> RunAsync(UserStory story, CancellationToken cancellationToken = default)
        {
            // Yield so the concurrent runs genuinely interleave (real AsyncLocal-per-run isolation + the
            // shared ConcurrentQueue under contention), not run to completion one after another.
            await Task.Yield();

            var ctx = MetricsContext.Current
                ?? throw new InvalidOperationException("Expected an ambient MetricsContext set by the orchestrator.");

            for (var i = 0; i < metricsPerRun; i++)
            {
                var metric = Metric(ctx.RunId, $"Agent{i}");
                shared.Add(metric);                            // live working set (bounded — may be evicted)
                MetricsContext.Current?.RunSink?.Add(metric);  // run-owned sink (persisted intact)
            }

            for (var i = 0; i < floodNoise; i++)
            {
                shared.Add(Metric($"noise-{ctx.RunId}-{i}", "Noise"));
            }

            return EmptyResult(story);
        }

        private static RunMetric Metric(string runId, string agent) =>
            new(runId, "ad-hoc", 0, agent, "claude-sonnet-4", "Anthropic",
                100, 50, 900, 0.01m, true, null, DateTimeOffset.UtcNow);

        private static PipelineResult EmptyResult(UserStory story) =>
            new(story,
                new RequirementSpec("(test)", string.Empty, [], [], [], [], [], [], AgentMetrics.Empty),
                new CodeArtifact("(none)", "Clean Architecture", [], null, AgentMetrics.Empty),
                new TestArtifact("xUnit", [], 0, 0, 0, 0, AgentMetrics.Empty),
                [],
                PipelineStatus.Done,
                AgentMetrics.Empty);
    }
}
