// Tests the tenant-explicit run list (IPipelineRunRepository.ListForTenantAsync) used by the desktop
// Overview. Like the cost summary, it uses IgnoreQueryFilters + an explicit tenant id (the circuit-safe
// path), so the DbContext's ITenantContext is irrelevant — proven by reading "t1" through a context
// whose tenant is a different value. Also covers newest-first ordering, the limit bound, and the no-op repo.
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

public sealed class PipelineRunListTests
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
            .UseInMemoryDatabase($"runlist-{Guid.NewGuid()}")
            .Options;

    // Persist one run under an explicit tenant + timestamp via the tenant-explicit write path.
    private static async Task SeedAsync(
        DbContextOptions<PipelineDbContext> options, string tenant, DateTimeOffset ts, string story)
    {
        await using var db = new PipelineDbContext(options, new FixedTenant(tenant));
        var runId = Guid.NewGuid();
        var zero = new AgentMetrics("p", "m", 0, 0, 0m, TimeSpan.Zero);
        var spec = new RequirementSpec("T", "S", [], [], [], [], [], [], zero);
        var code = new CodeArtifact("n", "a", [], null, zero);
        var tests = new TestArtifact("x", [], 0, 0, 0, 0, zero);
        var qa = new QaReport(1, true, false, [], [], zero);
        var result = new PipelineResult(
            new UserStory(story, 1, "en"), spec, code, tests, [qa], PipelineStatus.Done, zero);

        await new PipelineRunRepository(db, new FixedTenant(tenant))
            .SaveAsync(new PipelineRunRecord(runId, result, [], ts, ts), tenant);
    }

    [Fact]
    public async Task ListForTenant_ExcludesOtherTenants()
    {
        var options = NewOptions();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(options, "t1", now, "mine");
        await SeedAsync(options, "t2", now, "theirs");

        await using var db = new PipelineDbContext(options, new FixedTenant("x"));
        var runs = await new PipelineRunRepository(db, new FixedTenant("x")).ListForTenantAsync("t1");

        runs.Count.ShouldBe(1);
        runs[0].UserStoryPreview.ShouldBe("mine");
    }

    [Fact]
    public async Task ListForTenant_NewestFirst_BoundedByLimit()
    {
        var options = NewOptions();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(options, "t1", now.AddMinutes(-30), "oldest");
        await SeedAsync(options, "t1", now.AddMinutes(-10), "middle");
        await SeedAsync(options, "t1", now, "newest");

        await using var db = new PipelineDbContext(options, new FixedTenant("x"));
        var runs = await new PipelineRunRepository(db, new FixedTenant("x")).ListForTenantAsync("t1", limit: 2);

        runs.Count.ShouldBe(2);
        runs[0].UserStoryPreview.ShouldBe("newest");
        runs[1].UserStoryPreview.ShouldBe("middle");
    }

    [Fact]
    public async Task NullRepository_ListForTenant_ReturnsEmpty()
    {
        var runs = await new NullPipelineRunRepository().ListForTenantAsync("t1");

        runs.ShouldBeEmpty();
    }
}
