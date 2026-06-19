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

    [Fact]
    public async Task RunAsync_ModelClaimsConsistentButScoreBelowBar_OverriddenToInconsistent()
    {
        // Hallucinated verdict: isConsistent=true but score 0.50 (< 0.8 bar). The QA loop's exit condition
        // must NOT trust this — re-derived IsConsistent is false so the loop keeps iterating on bad output.
        const string json = """{"score":0.50,"isConsistent":true,"iterationNeeded":false,"issues":[],"recommendations":[]}""";
        var report = await Run(json);
        report.IsConsistent.ShouldBeFalse();
        report.Score.ShouldBe(0.50); // score is reported verbatim; only the verdict is hardened
    }

    [Fact]
    public async Task RunAsync_ModelClaimsConsistentButCriticalIssue_OverriddenToInconsistent()
    {
        const string json = """{"score":0.95,"isConsistent":true,"iterationNeeded":false,"issues":[{"severity":"Critical","category":"Security","description":"missing authz"}],"recommendations":[]}""";
        var report = await Run(json);
        report.IsConsistent.ShouldBeFalse(); // a Critical issue blocks convergence regardless of score
    }

    [Fact]
    public async Task RunAsync_HighScoreNoCriticalAndModelAgrees_StaysConsistent()
    {
        const string json = """{"score":0.92,"isConsistent":true,"iterationNeeded":false,"issues":[{"severity":"Minor","category":"Docs","description":"nit"}],"recommendations":[]}""";
        var report = await Run(json);
        report.IsConsistent.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_ModelSaysInconsistent_NeverFlippedTrueEvenWithHighScore()
    {
        // We only ever make the verdict STRICTER — a model "false" is preserved even at a passing score.
        const string json = """{"score":0.99,"isConsistent":false,"iterationNeeded":true,"issues":[],"recommendations":[]}""";
        var report = await Run(json);
        report.IsConsistent.ShouldBeFalse();
    }

    private static async Task<AgentOs.Domain.Qa.QaReport> Run(string json)
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(json));
        return await KcBenchHarness.BuildQa(llm).RunAsync(Spec(), Code(), Tests());
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
