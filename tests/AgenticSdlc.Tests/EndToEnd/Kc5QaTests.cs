// AgenticSdlc.Tests/EndToEnd/Kc5QaTests.cs
// Sprint 4 — KC5 QaAgent bench n=10.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Metrics;
using AgenticSdlc.Domain;
using AgenticSdlc.Domain.Code;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Requirements;
using AgenticSdlc.Domain.Testing;
using AgenticSdlc.Tests.Agents;
using AgenticSdlc.Tests.Pipeline;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.EndToEnd;

public class Kc5QaTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public async Task Kc5_QaConsistency_Iteration(int iteration)
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(Fixtures.QaPassJson));

        var agent = KcBenchHarness.BuildQa(llm);

        using var _ = MetricsContext.BeginScope($"KC5-run-{iteration}", "KC5", iteration);
        var report = await agent.RunAsync(SpecStub(), CodeStub(), TestStub());

        report.Score.ShouldBeInRange(0.0, 1.0);
        report.IsConsistent.ShouldBeTrue();
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

    private static TestArtifact TestStub()
        => new("xUnit",
               [new CodeFile("tests/ProductTests.cs", "// stub", "csharp")],
               1, 1, 1, 70, AgentMetrics.Empty);
}
