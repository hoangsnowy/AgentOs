// Tests PipelineRunRepository with EF Core InMemory (the jsonb column type is ignored — enough to verify the repo logic).
using AgenticSdlc.Application.Metrics;
using AgenticSdlc.Application.Persistence;
using AgenticSdlc.Domain;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Domain.Qa;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;
using AgenticSdlc.Infrastructure.Persistence;
using AgenticSdlc.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Persistence;

public sealed class PipelineRunRepositoryTests
{
    private static DbContextOptions<AgenticSdlcDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AgenticSdlcDbContext>()
            .UseInMemoryDatabase($"runs-{Guid.NewGuid()}")
            .Options;

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTripsResultAndMetrics()
    {
        var options = NewOptions();
        var runId = Guid.NewGuid();
        var record = SampleRecord(runId);

        await using (var db = new AgenticSdlcDbContext(options))
        {
            await new PipelineRunRepository(db).SaveAsync(record);
        }

        await using (var db = new AgenticSdlcDbContext(options))
        {
            var got = await new PipelineRunRepository(db).GetAsync(runId);

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
        await using var db = new AgenticSdlcDbContext(NewOptions());
        var got = await new PipelineRunRepository(db).GetAsync(Guid.NewGuid());
        got.ShouldBeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsSummariesNewestFirst_RespectingLimit()
    {
        var options = NewOptions();
        await using (var db = new AgenticSdlcDbContext(options))
        {
            var repo = new PipelineRunRepository(db);
            await repo.SaveAsync(SampleRecord(Guid.NewGuid(), createdAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
            await repo.SaveAsync(SampleRecord(Guid.NewGuid(), createdAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await using (var db = new AgenticSdlcDbContext(options))
        {
            var list = await new PipelineRunRepository(db).ListAsync(limit: 1);

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
