// Proves the keyless OfflineLlmClient returns output that actually drives the REAL specialist agents: each
// agent runs the offline response through its schema validation (requirement-spec/code-artifact/test-artifact
// v1) + DTO invariants and maps a domain artifact. This is what makes a no-key standalone / E2E run produce
// real artifacts instead of throwing — so the guarantee is locked by a test, not just by config.

using System.Threading.Tasks;
using AgentOs.Domain;
using AgentOs.Domain.Code;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Pipeline;
using AgentOs.Domain.Requirements;
using AgentOs.Domain.Testing;
using AgentOs.Modules.Llm;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Tests.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Llm;

public sealed class OfflineLlmClientTests
{
    private static readonly ILlmClientFactory Factory = AgentTestHelpers.FactoryReturning(new OfflineLlmClient());

    [Fact]
    public async Task RequirementAgent_OnOfflineClient_ParsesSchemaValidSpec()
    {
        var agent = new RequirementAgent(
            Factory, AgentTestHelpers.OptionsWith(new AgentsOptions()),
            AgentTestHelpers.Validator, AgentTestHelpers.NewCollector(), NullLogger<RequirementAgent>.Instance);

        var spec = await agent.RunAsync(new UserStory("Build a todo list app"));

        spec.Title.ShouldNotBeNullOrWhiteSpace();
        spec.Entities.Count.ShouldBeGreaterThan(0);
        spec.Endpoints.Count.ShouldBeGreaterThan(0);
        spec.Metrics.Provider.ShouldBe(OfflineLlmClient.ProviderName);
    }

    [Fact]
    public async Task CodingAgent_OnOfflineClient_ParsesSchemaValidCode()
    {
        var agent = new CodingAgent(
            Factory, AgentTestHelpers.OptionsWith(new AgentsOptions()),
            AgentTestHelpers.Validator, AgentTestHelpers.NewCollector(), NullLogger<CodingAgent>.Instance);

        var code = await agent.RunAsync(StubSpec());

        code.ProjectName.ShouldNotBeNullOrWhiteSpace();
        code.Files.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task TestingAgent_OnOfflineClient_ParsesSchemaValidTests()
    {
        var agent = new TestingAgent(
            Factory, AgentTestHelpers.OptionsWith(new AgentsOptions()),
            AgentTestHelpers.Validator, AgentTestHelpers.NewCollector(), NullLogger<TestingAgent>.Instance);

        var tests = await agent.RunAsync(StubSpec(), StubCode());

        tests.Files.Count.ShouldBeGreaterThan(0);
        tests.TotalCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task QaAgent_OnOfflineClient_ParsesConsistentReport()
    {
        var agent = new QaAgent(
            Factory, AgentTestHelpers.OptionsWith(new AgentsOptions()),
            AgentTestHelpers.NewCollector(), NullLogger<QaAgent>.Instance);

        var report = await agent.RunAsync(StubSpec(), StubCode(), StubTests());

        report.Score.ShouldBeInRange(0.0, 1.0);
        report.IsConsistent.ShouldBeTrue();
    }

    [Fact]
    public async Task RawLlmNode_OnOfflineClient_EchoesPromptWithoutThrowing()
    {
        var client = new OfflineLlmClient();
        var request = new LlmRequest("You are a helpful assistant.", "Summarize the plan.", "any-model");
        request.Validate();

        var response = await client.SendAsync(request);

        response.Provider.ShouldBe(OfflineLlmClient.ProviderName);
        response.CostUsd.ShouldBe(0m);
        response.Content.ShouldContain("offline");
    }

    [Fact]
    public async Task AgentRoleHint_RoutesEvenWhenSystemPromptOpeningLineIsReworded()
    {
        // A tenant prompt override removes the "You are the Requirement Agent" opening line; the AgentRole hint
        // (set by LlmAgentBase from its fixed PromptKey) must still route to the requirement payload.
        var client = new OfflineLlmClient();
        var request = new LlmRequest("As a senior analyst, produce the spec.", "Build a todo app", "m")
        {
            AgentRole = "Requirement",
        };
        request.Validate();

        var response = await client.SendAsync(request);

        response.Content.ShouldContain("acceptanceCriteria");   // the requirement payload, not the echo
    }

    private static RequirementSpec StubSpec() => new(
        Title: "T", Summary: "S", Stakeholders: [], FunctionalRequirements: [], NonFunctionalRequirements: [],
        Entities: [new EntityDescriptor("E", [])], Endpoints: [new EndpointDescriptor("GET", "/", "root")],
        AcceptanceCriteria: ["a", "b", "c"],
        Metrics: new AgentMetrics("Test", "m", 10, 5, 0.0001m, System.TimeSpan.FromMilliseconds(50)));

    private static CodeArtifact StubCode() => new(
        ProjectName: "P", Architecture: "Clean Architecture",
        Files: [new CodeFile("src/E.cs", "namespace P;")], Notes: null,
        Metrics: new AgentMetrics("Test", "m", 20, 10, 0.0002m, System.TimeSpan.FromMilliseconds(80)));

    private static TestArtifact StubTests() => new(
        Framework: "xUnit", Files: [new CodeFile("tests/ETests.cs", "namespace T;")],
        HappyPathCount: 1, EdgeCaseCount: 1, ErrorCaseCount: 1, EstimatedCoveragePercent: 60,
        Metrics: new AgentMetrics("Test", "m", 15, 8, 0.00015m, System.TimeSpan.FromMilliseconds(70)));
}
