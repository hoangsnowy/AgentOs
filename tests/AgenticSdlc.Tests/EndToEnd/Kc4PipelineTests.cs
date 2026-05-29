// AgenticSdlc.Tests/EndToEnd/Kc4PipelineTests.cs
// Sprint 4 — KC4 full pipeline bench n=10 (Requirement → Coding → Testing → QA).

using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Tests.Pipeline;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.EndToEnd;

public class Kc4PipelineTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public async Task Kc4_FullPipeline_Iteration(int iteration)
    {
        var responses = new[]
        {
            new CannedResponse(Fixtures.RequirementJson),
            new CannedResponse(Fixtures.CodingJson),
            new CannedResponse(Fixtures.TestingJson),
            new CannedResponse(Fixtures.QaPassJson),
        };
        var sequenced = new SequencedLlmClient(responses);
        var orchestrator = KcBenchHarness.BuildOrchestrator(sequenced);

        using var _ = MetricsContext.BeginScope($"KC4-run-{iteration}", "KC4", iteration);
        var result = await orchestrator.RunAsync(new UserStory("Story KC4"));

        result.Status.ShouldBe(PipelineStatus.Done);
        result.IterationCount.ShouldBeLessThanOrEqualTo(3);
        result.QaHistory[^1].Score.ShouldBeGreaterThanOrEqualTo(0.6);
    }
}
