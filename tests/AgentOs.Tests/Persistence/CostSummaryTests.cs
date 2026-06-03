// Tests the tenant-explicit cost aggregation (IPipelineRunRepository.GetCostSummaryForTenantAsync).
// EF Core InMemory; the method uses IgnoreQueryFilters + an explicit tenant id (the circuit-safe path),
// so the DbContext's ITenantContext is irrelevant to the read — proven here by passing a tenant that
// owns no rows yet still reading "t1".
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Modules.Pipeline.Persistence.Repositories;
using AgentOs.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class CostSummaryTests
{
    private sealed class FixedTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
        public string? UserId => "u";
        public string? UserName => "u";
        public IReadOnlyList<string> Roles { get; } = ["admin"];
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private static DbContextOptions<PipelineDbContext> NewOptions() =>
        new DbContextOptionsBuilder<PipelineDbContext>()
            .UseInMemoryDatabase($"cost-{Guid.NewGuid()}")
            .Options;

    // Persist one pipeline run (and its LLM-call metrics) under a tenant + timestamp.
    private static async Task SeedAsync(
        DbContextOptions<PipelineDbContext> options, string tenant, DateTimeOffset ts,
        params (string Agent, string Provider, string Model, int In, int Out, decimal Cost)[] calls)
    {
        var tc = new FixedTenant(tenant);
        await using var db = new PipelineDbContext(options, tc);

        var runId = Guid.NewGuid();
        var zero = new AgentMetrics("p", "m", 0, 0, 0m, TimeSpan.Zero);
        var spec = new RequirementSpec("T", "S", [], [], [], [], [], [], zero);
        var code = new CodeArtifact("n", "a", [], null, zero);
        var tests = new TestArtifact("x", [], 0, 0, 0, 0, zero);
        var qa = new QaReport(1, true, false, [], [], zero);
        var result = new PipelineResult(
            new UserStory("s", 1, "en"), spec, code, tests, [qa], PipelineStatus.Done, zero);

        var metrics = calls
            .Select((c, i) => new RunMetric(
                runId.ToString(), "ad-hoc", i, c.Agent, c.Model, c.Provider,
                c.In, c.Out, 100, c.Cost, true, null, ts))
            .ToList();

        await new PipelineRunRepository(db, tc)
            .SaveAsync(new PipelineRunRecord(runId, result, metrics, ts, ts));
    }

    [Fact]
    public async Task GetCostSummary_GroupsByAgentProviderModel_AndComputesTotals()
    {
        var options = NewOptions();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(options, "t1", now,
            ("RequirementAgent", "Claude", "claude-sonnet-4", 100, 50, 0.01m),
            ("CodingAgent", "AzureOpenAI", "gpt-4.1", 200, 100, 0.02m));
        await SeedAsync(options, "t1", now,
            ("RequirementAgent", "Claude", "claude-sonnet-4", 100, 50, 0.01m));

        await using var db = new PipelineDbContext(options, new FixedTenant("ignored"));
        var sum = await new PipelineRunRepository(db, new FixedTenant("ignored"))
            .GetCostSummaryForTenantAsync("t1");

        sum.TotalCostUsd.ShouldBe(0.04m);
        sum.TotalTokensIn.ShouldBe(400);
        sum.TotalTokensOut.ShouldBe(200);
        sum.CallCount.ShouldBe(3);
        sum.RunCount.ShouldBe(2);

        var req = sum.ByAgent.First(b => b.Key == "RequirementAgent");
        req.CostUsd.ShouldBe(0.02m);
        req.Calls.ShouldBe(2);
        sum.ByProvider.Count.ShouldBe(2);
        sum.ByModel.First(b => b.Key == "claude-sonnet-4").TokensIn.ShouldBe(200);

        // Buckets are ordered by cost, descending.
        sum.ByAgent[0].CostUsd.ShouldBeGreaterThanOrEqualTo(sum.ByAgent[^1].CostUsd);
    }

    [Fact]
    public async Task GetCostSummary_ExcludesOtherTenants()
    {
        var options = NewOptions();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(options, "t1", now, ("A", "Claude", "m1", 10, 10, 0.01m));
        await SeedAsync(options, "t2", now, ("A", "Claude", "m1", 10, 10, 0.99m));

        await using var db = new PipelineDbContext(options, new FixedTenant("x"));
        var sum = await new PipelineRunRepository(db, new FixedTenant("x"))
            .GetCostSummaryForTenantAsync("t1");

        sum.TotalCostUsd.ShouldBe(0.01m);
        sum.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetCostSummary_SinceFilter_DropsOlderRows()
    {
        var options = NewOptions();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(options, "t1", now.AddDays(-40), ("A", "Claude", "m1", 10, 10, 0.05m));
        await SeedAsync(options, "t1", now.AddDays(-1), ("A", "Claude", "m1", 10, 10, 0.02m));

        await using var db = new PipelineDbContext(options, new FixedTenant("x"));
        var sum = await new PipelineRunRepository(db, new FixedTenant("x"))
            .GetCostSummaryForTenantAsync("t1", now.AddDays(-7));

        sum.CallCount.ShouldBe(1);
        sum.TotalCostUsd.ShouldBe(0.02m);
        sum.ByDay.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetCostSummary_NoData_ReturnsEmpty()
    {
        var options = NewOptions();
        await using var db = new PipelineDbContext(options, new FixedTenant("x"));
        var sum = await new PipelineRunRepository(db, new FixedTenant("x"))
            .GetCostSummaryForTenantAsync("t1");

        sum.CallCount.ShouldBe(0);
        sum.TotalCostUsd.ShouldBe(0m);
        sum.ByAgent.ShouldBeEmpty();
    }

    [Fact]
    public async Task NullRepository_GetCostSummary_ReturnsEmpty()
    {
        var sum = await new NullPipelineRunRepository().GetCostSummaryForTenantAsync("t1");

        sum.CallCount.ShouldBe(0);
        sum.ByAgent.ShouldBeEmpty();
    }
}
