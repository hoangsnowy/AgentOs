// AgenticSdlc.Tests/Pipeline/PipelineEndToEndTests.cs
// Phase 5 — End-to-end tests for PipelineOrchestrator using SequencedLlmClient.
// Verifies KC4 (the 5-agent pipeline flow + QA loop) runs correctly from UserStory → PipelineResult.

using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Agents;
using AgenticSdlc.Modules.Pipeline.Pipeline;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Pipeline;
using AgenticSdlc.Modules.Llm;
using AgenticSdlc.Modules.Pipeline.Orchestration;
using AgenticSdlc.Tests.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Pipeline;

public class PipelineEndToEndTests
{
    [Fact]
    public async Task Pipeline_QaPassFirstIteration_AggregatesAllMetrics()
    {
        // 4 canned response: requirement → coding → testing → qa(pass)
        var responses = new[]
        {
            new CannedResponse(Fixtures.RequirementJson, 250, 380),
            new CannedResponse(Fixtures.CodingJson, 600, 900),
            new CannedResponse(Fixtures.TestingJson, 700, 800),
            new CannedResponse(Fixtures.QaPassJson, 400, 200),
        };

        var sequenced = new SequencedLlmClient(responses);
        var orchestrator = BuildOrchestrator(sequenced);

        var result = await orchestrator.RunAsync(new UserStory("Product management system: admin CRUD, users browse."));

        result.Status.ShouldBe(PipelineStatus.Done);
        result.IterationCount.ShouldBe(1);
        result.Spec.Entities.Count.ShouldBeGreaterThan(0);
        result.Code.Files.Count.ShouldBeGreaterThan(0);
        result.Tests.TotalCount.ShouldBeGreaterThan(0);
        result.QaHistory[0].IsConsistent.ShouldBeTrue();

        // Sum metrics: 250+600+700+400 input, 380+900+800+200 output
        result.TotalMetrics.InputTokens.ShouldBe(1950);
        result.TotalMetrics.OutputTokens.ShouldBe(2280);

        sequenced.CapturedRequests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Pipeline_QaFailsThenPasses_TwoIterations()
    {
        var responses = new[]
        {
            new CannedResponse(Fixtures.RequirementJson),
            // Iteration 1: coding → testing → qa(fail)
            new CannedResponse(Fixtures.CodingJson),
            new CannedResponse(Fixtures.TestingJson),
            new CannedResponse(Fixtures.QaFailJson),
            // Iteration 2: coding → testing → qa(pass)
            new CannedResponse(Fixtures.CodingJson),
            new CannedResponse(Fixtures.TestingJson),
            new CannedResponse(Fixtures.QaPassJson),
        };

        var orchestrator = BuildOrchestrator(new SequencedLlmClient(responses));

        var result = await orchestrator.RunAsync(new UserStory("Story", NMax: 3));

        result.Status.ShouldBe(PipelineStatus.Done);
        result.IterationCount.ShouldBe(2);
        result.QaHistory[0].IsConsistent.ShouldBeFalse();
        result.QaHistory[1].IsConsistent.ShouldBeTrue();
    }

    [Fact]
    public async Task Pipeline_MalformedRequirementResponse_ReturnsFailed()
    {
        var responses = new[]
        {
            new CannedResponse("not json at all"),
        };

        var orchestrator = BuildOrchestrator(new SequencedLlmClient(responses));

        var result = await orchestrator.RunAsync(new UserStory("Story"));

        result.Status.ShouldBe(PipelineStatus.Failed);
        result.IterationCount.ShouldBe(0);
    }

    private static PipelineOrchestrator BuildOrchestrator(ILlmClient sequenced)
    {
        var factory = Substitute.For<ILlmClientFactory>();
        factory.Create(Arg.Any<string>()).Returns(sequenced);

        var agentsOpts = Options.Create(new AgentsOptions());
        var pipelineOpts = Options.Create(new PipelineOptions { MaxIterations = 3 });

        var validator = AgentTestHelpers.Validator;
        var collector = AgentTestHelpers.NewCollector();
        var requirement = new RequirementAgent(factory, agentsOpts, validator, collector, NullLogger<RequirementAgent>.Instance);
        var coding = new CodingAgent(factory, agentsOpts, validator, collector, NullLogger<CodingAgent>.Instance);
        var testing = new TestingAgent(factory, agentsOpts, validator, collector, NullLogger<TestingAgent>.Instance);
        var qa = new QaAgent(factory, agentsOpts, collector, NullLogger<QaAgent>.Instance);

        return new PipelineOrchestrator(requirement, coding, testing, qa, pipelineOpts, NullLogger<PipelineOrchestrator>.Instance);
    }
}

internal static class Fixtures
{
    public const string RequirementJson = """
        {
          "title": "Product management",
          "summary": "Admin CRUD products; users browse by category.",
          "stakeholders": ["admin", "customer"],
          "functionalRequirements": [
            "Admin creates/updates/deletes products with a unique SKU",
            "Users browse by category + keyword"
          ],
          "nonFunctionalRequirements": ["p95 < 200ms", "Supports 1000 RPS"],
          "entities": [
            { "name": "Product", "fields": ["id: Guid", "sku: string", "name: string", "categoryId: Guid", "price: decimal"], "notes": "SKU unique" },
            { "name": "Category", "fields": ["id: Guid", "name: string"], "notes": null }
          ],
          "endpoints": [
            { "method": "POST", "path": "/products", "purpose": "Create a product", "authRequired": true },
            { "method": "GET", "path": "/products/{id}", "purpose": "View a single product", "authRequired": false },
            { "method": "GET", "path": "/products", "purpose": "Browse by category", "authRequired": false }
          ],
          "acceptanceCriteria": [
            "POST /products requires admin auth, returns 401 if missing",
            "Duplicate SKU → returns 409 Conflict",
            "GET /products?categoryId=... returns the correct category"
          ]
        }
        """;

    public const string CodingJson = """
        {
          "projectName": "ProductCatalog",
          "architecture": "Clean Architecture",
          "files": [
            { "path": "src/Domain/Product.cs", "content": "namespace ProductCatalog.Domain;\npublic sealed record Product(Guid Id, string Sku, string Name, Guid CategoryId, decimal Price);", "language": "csharp" },
            { "path": "src/Domain/Category.cs", "content": "namespace ProductCatalog.Domain;\npublic sealed record Category(Guid Id, string Name);", "language": "csharp" },
            { "path": "src/Api/Program.cs", "content": "var app = WebApplication.CreateBuilder(args).Build();\napp.MapPost(\"/products\", () => {}); app.MapGet(\"/products/{id}\", (Guid id) => {}); app.MapGet(\"/products\", () => {});\napp.Run();", "language": "csharp" }
          ],
          "notes": "Auth/persistence are stubs — Phase 5 will add EF Core."
        }
        """;

    public const string TestingJson = """
        {
          "framework": "xUnit",
          "files": [
            { "path": "tests/ProductTests.cs", "content": "using Xunit; using Shouldly;\nnamespace ProductCatalog.Tests;\npublic class ProductTests {\n  [Fact] public void Create_Valid_OK() { /* ... */ }\n  [Theory][InlineData(\"\")][InlineData(\"   \")] public void Sku_Blank_Throws(string sku) { /* ... */ }\n  [Fact] public void Sku_Duplicate_Conflict() { /* ... */ }\n}", "language": "csharp" }
          ],
          "happyPathCount": 1,
          "edgeCaseCount": 2,
          "errorCaseCount": 1,
          "estimatedCoveragePercent": 75
        }
        """;

    public const string QaPassJson = """
        {
          "score": 0.92,
          "isConsistent": true,
          "iterationNeeded": false,
          "issues": [
            { "severity": "Minor", "category": "TestCoverage", "description": "Missing test for the NFR p95 latency", "location": "tests/ProductTests.cs" }
          ],
          "recommendations": ["Add a load-profile test for p95"]
        }
        """;

    public const string QaFailJson = """
        {
          "score": 0.65,
          "isConsistent": false,
          "iterationNeeded": true,
          "issues": [
            { "severity": "Critical", "category": "RequirementCoverage", "description": "The POST /products endpoint is missing an authorization check", "location": "src/Api/Program.cs" },
            { "severity": "Major", "category": "TestCoverage", "description": "No test yet for the duplicate-SKU 409", "location": null }
          ],
          "recommendations": ["Add admin auth middleware", "Add a duplicate-SKU test"]
        }
        """;
}
