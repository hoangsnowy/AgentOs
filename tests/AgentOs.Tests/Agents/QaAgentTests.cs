// QaAgent has no JSON schema (its output is advisory), so a malformed LLM issue can arrive with missing
// severity/category/description. The agent must coalesce those to safe defaults rather than building a
// QaIssue with null fields — a regression guard for the schema-less null-mapping bug.

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Tests.EndToEnd;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Agents;

public sealed class QaAgentTests
{
    [Fact]
    public async Task RunAsync_IssueMissingRequiredFields_CoalescesToDefaultsNotNulls()
    {
        // The issue carries only a location — severity/category/description are absent from the LLM JSON.
        const string json = """
            {"score":0.5,"isConsistent":false,"iterationNeeded":true,
             "issues":[{"location":"src/Foo.cs"}],"recommendations":[]}
            """;
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(json));
        var agent = KcBenchHarness.BuildQa(llm);

        var report = await agent.RunAsync(Spec(), Code(), Tests());

        report.Issues.Count.ShouldBe(1);
        var issue = report.Issues[0];
        issue.Severity.ShouldBe("info");        // coalesced, not null
        issue.Category.ShouldBe("general");     // coalesced, not null
        issue.Description.ShouldBe("");          // coalesced, not null
        issue.Location.ShouldBe("src/Foo.cs");  // preserved
    }

    private static RequirementSpec Spec()
        => new("T", "S", [], [], [],
               [new EntityDescriptor("Product", ["id"], null)],
               [new EndpointDescriptor("POST", "/products", "p", true)],
               ["a", "b", "c"], AgentMetrics.Empty);

    private static CodeArtifact Code()
        => new("ProductCatalog", "Clean Architecture",
               [new CodeFile("src/Domain/Product.cs", "// stub", "csharp")],
               "", AgentMetrics.Empty);

    private static TestArtifact Tests()
        => new("xUnit",
               [new CodeFile("tests/ProductTests.cs", "// stub", "csharp")],
               1, 1, 1, 70, AgentMetrics.Empty);
}
