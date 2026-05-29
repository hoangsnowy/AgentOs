// AgenticSdlc.Tests/Agents/RequirementAgentTests.cs
// Phase 4 — Unit tests for RequirementAgent.

using System.Threading;
using System.Threading.Tasks;
using AgenticSdlc.Modules.Pipeline.Agents;
using AgenticSdlc.Domain.Llm;
using AgenticSdlc.Domain.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgenticSdlc.Tests.Agents;

public class RequirementAgentTests
{
    [Fact]
    public async Task RunAsync_ValidJson_MapsToSpec()
    {
        var json = """
            {
              "title": "Product management",
              "summary": "Allows admin to CRUD products.",
              "stakeholders": ["admin", "customer"],
              "functionalRequirements": ["Admin creates a product"],
              "nonFunctionalRequirements": ["p95 < 200ms"],
              "entities": [{"name":"Product","fields":["id: Guid","sku: string"],"notes":null}],
              "endpoints": [{"method":"POST","path":"/products","purpose":"Create a product","authRequired":true}],
              "acceptanceCriteria": ["SKU unique","Validate price > 0","Authorization admin"]
            }
            """;

        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(json));

        var agent = new RequirementAgent(
            AgentTestHelpers.FactoryReturning(llm),
            AgentTestHelpers.OptionsWith(new AgentsOptions()),
            AgentTestHelpers.Validator,
            AgentTestHelpers.NewCollector(),
            NullLogger<RequirementAgent>.Instance);

        var spec = await agent.RunAsync(new UserStory("Product management system"));

        spec.Title.ShouldBe("Product management");
        spec.Entities.Count.ShouldBe(1);
        spec.Entities[0].Name.ShouldBe("Product");
        spec.Endpoints[0].AuthRequired.ShouldBeTrue();
        spec.AcceptanceCriteria.Count.ShouldBe(3);
        spec.Metrics.Provider.ShouldBe("Mock");
        spec.Metrics.InputTokens.ShouldBe(100);
    }

    [Fact]
    public async Task RunAsync_MissingEntities_ThrowsLlmException()
    {
        var json = """
            {
              "title": "X",
              "summary": "Y",
              "stakeholders": [],
              "functionalRequirements": [],
              "nonFunctionalRequirements": [],
              "entities": [],
              "endpoints": [{"method":"GET","path":"/","purpose":"root","authRequired":false}],
              "acceptanceCriteria": []
            }
            """;

        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(json));

        var agent = new RequirementAgent(
            AgentTestHelpers.FactoryReturning(llm),
            AgentTestHelpers.OptionsWith(new AgentsOptions()),
            AgentTestHelpers.Validator,
            AgentTestHelpers.NewCollector(),
            NullLogger<RequirementAgent>.Instance);

        var ex = await Should.ThrowAsync<LlmException>(() => agent.RunAsync(new UserStory("story")));
        ex.Message.ShouldContain("entities");
    }

    [Fact]
    public async Task RunAsync_MalformedJson_ThrowsLlmException()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse("not json at all"));

        var agent = new RequirementAgent(
            AgentTestHelpers.FactoryReturning(llm),
            AgentTestHelpers.OptionsWith(new AgentsOptions()),
            AgentTestHelpers.Validator,
            AgentTestHelpers.NewCollector(),
            NullLogger<RequirementAgent>.Instance);

        await Should.ThrowAsync<LlmException>(() => agent.RunAsync(new UserStory("story")));
    }

    [Fact]
    public async Task RunAsync_RespectsConfiguredModel()
    {
        var json = """{"title":"T","summary":"S","stakeholders":[],"functionalRequirements":[],"nonFunctionalRequirements":[],"entities":[{"name":"E","fields":[]}],"endpoints":[{"method":"GET","path":"/","purpose":"p","authRequired":false}],"acceptanceCriteria":["a","b","c"]}""";
        LlmRequest? captured = null;

        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Do<LlmRequest>(r => captured = r), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(json));

        var opts = new AgentsOptions
        {
            Requirement = new AgentOptions { Provider = "Mock", Model = "claude-test-1", Temperature = 0.42, MaxTokens = 999 },
        };
        var agent = new RequirementAgent(AgentTestHelpers.FactoryReturning(llm), AgentTestHelpers.OptionsWith(opts), AgentTestHelpers.Validator, AgentTestHelpers.NewCollector(), NullLogger<RequirementAgent>.Instance);

        await agent.RunAsync(new UserStory("story"));

        captured.ShouldNotBeNull();
        captured!.Model.ShouldBe("claude-test-1");
        captured.Temperature.ShouldBe(0.42);
        captured.MaxTokens.ShouldBe(999);
    }
}
