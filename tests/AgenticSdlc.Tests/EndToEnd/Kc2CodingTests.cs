// AgenticSdlc.Tests/EndToEnd/Kc2CodingTests.cs
// Sprint 4 — KC2 CodingAgent bench n=10.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Tests.Agents;
using AgenticSdlc.Tests.Pipeline;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.EndToEnd;

public class Kc2CodingTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public async Task Kc2_CodeGeneration_Iteration(int iteration)
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(Fixtures.CodingJson));

        var agent = KcBenchHarness.BuildCoding(llm);

        var spec = SpecStub();
        using var _ = MetricsContext.BeginScope($"KC2-run-{iteration}", "KC2", iteration);
        var code = await agent.RunAsync(spec);

        code.Files.Count.ShouldBeGreaterThanOrEqualTo(3);
        code.ProjectName.ShouldContain("Product");
    }

    private static RequirementSpec SpecStub()
        => new(
            Title: "T", Summary: "S",
            Stakeholders: [], FunctionalRequirements: [], NonFunctionalRequirements: [],
            Entities: [new EntityDescriptor("Product", ["id"], null)],
            Endpoints: [new EndpointDescriptor("POST", "/products", "create", true)],
            AcceptanceCriteria: ["a", "b", "c"],
            Metrics: AgenticSdlc.Domain.AgentMetrics.Empty);
}
