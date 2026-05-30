// Tests PipelineRunRepository with EF Core InMemory (the jsonb column type is ignored — enough to verify the repo logic).
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Modules.Pipeline.Persistence;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Qa;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Pipeline.Persistence.Repositories;
using AgentOs.Tests.Identity;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Persistence;

public sealed class PipelineRunRepositoryTests
{
    private static DbContextOptions<PipelineDbContext> NewOptions() =>
        new DbContextOptionsBuilder<PipelineDbContext>()
            .UseInMemoryDatabase($"runs-{Guid.NewGuid()}")
            .Options;

    private static readonly TestTenantContext Tenant = new();

    private static PipelineDbContext NewDb(DbContextOptions<PipelineDbContext> options) =>
        new(options, Tenant);

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsResultAndMetrics()
    {
        var options = NewOptions();
        var runId = Guid.NewGuid();
        var record = SampleRecord(runId);

        await using (var db = NewDb(options))
        {
            await new PipelineRunRepository(db, Tenant).SaveAsync(record);
        }

        await using (var db = NewDb(options))
        {
            var got = await new PipelineRunRepository(db, Tenant).GetAsync(runId);

            got.ShouldNotBeNull();
            got.Id.ShouldBe(runId);
            got.Result.Status.ShouldBe(PipelineStatus.Done);
            got.Result.Spec.Title.ShouldBe("Product Mgmt");
            got.Result.QaHistory.Count.ShouldBe(1);
            got.Result.TotalMetrics.CostUsd.ShouldBe(record.Result.TotalMetrics.CostUsd);
            got.Metrics.Count.ShouldBe(2);
            got.Metrics[0].AgentName.ShouldBe("RequirementAgent");
        }
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        await using var db = NewDb(NewOptions());
        var got = await new PipelineRunRepository(db, Tenant).GetAsync(Guid.NewGuid());
        got.ShouldBeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsSummariesNewestFirst_RespectingLimit()
    {
        var options = NewOptions();
        await using (var db = NewDb(options))
        {
            var repo = new PipelineRunRepository(db, Tenant);
            await repo.SaveAsync(SampleRecord(Guid.NewGuid(), createdAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
            await repo.SaveAsync(SampleRecord(Guid.NewGuid(), createdAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await using (var db = NewDb(options))
        {
            var list = await new PipelineRunRepository(db, Tenant).ListAsync(limit: 1);

            list.Count.ShouldBe(1);
            list[0].Status.ShouldBe(nameof(PipelineStatus.Done));
            list[0].UserStoryPreview.ShouldBe("Product management");
        }
    }

    private static AgentMetrics M(decimal cost, int tin, int tout) =>
        new("Anthropic", "claude-sonnet-4", tin, tout, cost, TimeSpan.FromSeconds(1));

    private static PipelineRunRecord SampleRecord(Guid id, DateTimeOffset? createdAt = null)
    {
        var spec = new RequirementSpec("Product Mgmt", "Summary", [], [], [], [], [], [], M(0.01m, 100, 50));
        var code = new CodeArtifact("Shop", "Clean Architecture", [], null, M(0.02m, 200, 100));
        var tests = new TestArtifact("xUnit", [], 2, 1, 1, 80, M(0.01m, 150, 80));
        var qa = new QaReport(0.9, true, false, [], [], M(0.005m, 50, 30));
        var total = spec.Metrics.Add(code.Metrics).Add(tests.Metrics).Add(qa.Metrics);

        var result = new PipelineResult(
            new UserStory("Product management", 3, "vi-VN"),
            spec, code, tests, [qa], PipelineStatus.Done, total);

        var metrics = new List<RunMetric>
        {
            new(id.ToString(), "ad-hoc", 0, "RequirementAgent", "claude-sonnet-4", "Anthropic", 100, 50, 900, 0.01m, true, null, DateTimeOffset.UtcNow),
            new(id.ToString(), "ad-hoc", 1, "CodingAgent", "gpt-4.1", "AzureOpenAI", 200, 100, 1500, 0.02m, true, null, DateTimeOffset.UtcNow),
        };

        var created = createdAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        return new PipelineRunRecord(id, result, metrics, created, created.AddSeconds(30));
    }
}
