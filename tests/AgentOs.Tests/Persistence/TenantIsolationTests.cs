// Proves the DbContext's global query filter isolates pipeline_runs + orchestrations between
// tenants: a row inserted under tenant A is invisible to a repo running as tenant B (and vice
// versa). Uses EF Core InMemory — query filters work identically there.

using AgentOs.SharedKernel.Identity;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Pipeline.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class TenantIsolationTests
{
    private sealed class FakeTenant(string id) : ITenantContext
    {
        public string TenantId { get; } = id;
        public string? UserId => "test";
        public string? UserName => "test";
        public IReadOnlyList<string> Roles { get; } = new[] { "admin" };
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private static DbContextOptions<PipelineDbContext> SharedOptions() =>
        new DbContextOptionsBuilder<PipelineDbContext>()
            .UseInMemoryDatabase($"tenants-{Guid.NewGuid()}")
            .Options;

    [Fact]
    public async Task PipelineRuns_OneTenantCannotSeeAnotherTenantsRuns()
    {
        var options = SharedOptions();
        var alice = new FakeTenant("alice");
        var bob = new FakeTenant("bob");
        var aliceRun = Guid.NewGuid();
        var bobRun = Guid.NewGuid();

        await using (var db = new PipelineDbContext(options, alice))
        {
            await new PipelineRunRepository(db, alice).SaveAsync(SampleRecord(aliceRun));
        }
        await using (var db = new PipelineDbContext(options, bob))
        {
            await new PipelineRunRepository(db, bob).SaveAsync(SampleRecord(bobRun));
        }

        await using (var db = new PipelineDbContext(options, alice))
        {
            var repo = new PipelineRunRepository(db, alice);
            (await repo.GetAsync(aliceRun)).ShouldNotBeNull();
            (await repo.GetAsync(bobRun)).ShouldBeNull("Bob's run must be invisible to Alice");
            (await repo.ListAsync()).Count.ShouldBe(1);
        }
        await using (var db = new PipelineDbContext(options, bob))
        {
            var repo = new PipelineRunRepository(db, bob);
            (await repo.GetAsync(bobRun)).ShouldNotBeNull();
            (await repo.GetAsync(aliceRun)).ShouldBeNull("Alice's run must be invisible to Bob");
        }
    }

    // OrchestrationRepository reads are now Dapper-only (Postgres); EF InMemory can't exercise the raw
    // SQL, and on the Dapper path tenant isolation is enforced by the explicit `WHERE "TenantId" = @tenantId`
    // in the query rather than the EF global filter. Verify against real Postgres in the full-stack E2E.
    [Fact(Skip = "OrchestrationRepository reads are Dapper-only (Postgres); verify tenant isolation via full-stack E2E.")]
    public async Task Orchestrations_OneTenantCannotSeeAnotherTenantsGraphs()
    {
        var options = SharedOptions();
        var alice = new FakeTenant("alice");
        var bob = new FakeTenant("bob");

        await using (var db = new PipelineDbContext(options, alice))
        {
            await new OrchestrationRepository(db, alice).UpsertAsync(
                new OrchestrationRecord("g1", "Alice graph", null, "{}", DateTimeOffset.UtcNow));
        }
        await using (var db = new PipelineDbContext(options, bob))
        {
            await new OrchestrationRepository(db, bob).UpsertAsync(
                new OrchestrationRecord("g2", "Bob graph", null, "{}", DateTimeOffset.UtcNow));
        }

        await using (var db = new PipelineDbContext(options, alice))
        {
            var list = await new OrchestrationRepository(db, alice).ListAsync();
            list.Count.ShouldBe(1);
            list[0].Name.ShouldBe("Alice graph");
        }
        await using (var db = new PipelineDbContext(options, bob))
        {
            var list = await new OrchestrationRepository(db, bob).ListAsync();
            list.Count.ShouldBe(1);
            list[0].Name.ShouldBe("Bob graph");
        }
    }

    private static AgentMetrics M(decimal cost, int tin, int tout) =>
        new("Anthropic", "claude-sonnet-4", tin, tout, cost, TimeSpan.FromSeconds(1));

    private static PipelineRunRecord SampleRecord(Guid id)
    {
        var spec = new RequirementSpec("Title", "Summary", [], [], [], [], [], [], M(0.01m, 100, 50));
        var code = new CodeArtifact("App", "Clean", [], null, M(0.02m, 200, 100));
        var tests = new TestArtifact("xUnit", [], 1, 0, 1, 80, M(0.01m, 150, 80));
        var qa = new QaReport(0.9, true, false, [], [], M(0.005m, 50, 30));
        var total = spec.Metrics.Add(code.Metrics).Add(tests.Metrics).Add(qa.Metrics);
        var result = new PipelineResult(
            new UserStory("Story", 3, "en-US"),
            spec, code, tests, [qa], PipelineStatus.Done, total);
        var metrics = new List<RunMetric>
        {
            new(id.ToString(), "ad-hoc", 0, "RequirementAgent", "claude-sonnet-4", "Anthropic", 100, 50, 900, 0.01m, true, null, DateTimeOffset.UtcNow),
        };
        var created = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new PipelineRunRecord(id, result, metrics, created, created.AddSeconds(30));
    }
}
