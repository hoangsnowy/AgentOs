// AgenticSdlc.Tests/EndToEnd/Kc3TestingTests.cs
// Sprint 4 — KC3 TestingAgent bench n=10.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Domain;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Tests.Agents;
using AgenticSdlc.Tests.Pipeline;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.EndToEnd;

public class Kc3TestingTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public async Task Kc3_TestGeneration_Iteration(int iteration)
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(Fixtures.TestingJson));

        var agent = KcBenchHarness.BuildTesting(llm);

        using var _ = MetricsContext.BeginScope($"KC3-run-{iteration}", "KC3", iteration);
        var tests = await agent.RunAsync(SpecStub(), CodeStub());

        tests.Framework.ShouldBe("xUnit");
        tests.TotalCount.ShouldBeGreaterThan(0);
        tests.EstimatedCoveragePercent.ShouldBeGreaterThanOrEqualTo(60);
    }

    private static RequirementSpec SpecStub()
        => new("T", "S", [], [], [],
               [new EntityDescriptor("Product", ["id"], null)],
               [new EndpointDescriptor("POST", "/products", "p", true)],
               ["a", "b", "c"], AgentMetrics.Empty);

    private static CodeArtifact CodeStub()
        => new("ProductCatalog", "Clean Architecture",
               [new CodeFile("src/Domain/Product.cs", "// stub", "csharp")],
               "", AgentMetrics.Empty);
}
