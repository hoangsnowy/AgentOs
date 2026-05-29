// AgentOs.Tests/EndToEnd/Kc1RequirementTests.cs
// KC1 RequirementAgent bench n=10 (NSubstitute stub for deterministic output).

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Modules.Pipeline.Metrics;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Pipeline;
using AgentOs.Tests.Agents;
using AgentOs.Tests.Pipeline;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.EndToEnd;

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

