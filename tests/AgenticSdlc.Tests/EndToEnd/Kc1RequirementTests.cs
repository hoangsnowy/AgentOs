// AgenticSdlc.Tests/EndToEnd/Kc1RequirementTests.cs
// Sprint 4 — KC1 RequirementAgent bench n=10 (deterministic Mock).

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Tests.Agents;
using AgenticSdlc.Tests.Pipeline;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.EndToEnd;

public class Kc1RequirementTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public async Task Kc1_RequirementAnalysis_Iteration(int iteration)
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(Fixtures.RequirementJson));

        var agent = KcBenchHarness.BuildRequirement(llm);

        using var _ = MetricsContext.BeginScope($"KC1-run-{iteration}", "KC1", iteration);
        var spec = await agent.RunAsync(new UserStory("Story KC1"));

        spec.Entities.Count.ShouldBeGreaterThanOrEqualTo(2);
        spec.Endpoints.Count.ShouldBeGreaterThanOrEqualTo(2);
        spec.AcceptanceCriteria.Count.ShouldBeGreaterThanOrEqualTo(3);
    }
}

