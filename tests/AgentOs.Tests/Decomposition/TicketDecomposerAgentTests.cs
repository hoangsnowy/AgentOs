// Bootstrap (slice 2) — the LLM right-size pass: parse + taxonomy-filter, ai-gate sync, and the
// seed fallback when the model misbehaves (the property that keeps the bootstrap flow alive).

using System.Threading;
using System.Threading.Tasks;
using AgentOs.Domain.Llm;
using AgentOs.Domain.Workspaces;
using AgentOs.Modules.Pipeline.Agents;
using AgentOs.Modules.Pipeline.Decomposition;
using AgentOs.Tests.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AgentOs.Tests.Decomposition;

public sealed class TicketDecomposerAgentTests
{
    private static TicketDecomposerAgent Build(string llmContent)
    {
        var llm = Substitute.For<ILlmClient>();
        llm.SendAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>())
           .Returns(AgentTestHelpers.StubResponse(llmContent));
        return new TicketDecomposerAgent(
            AgentTestHelpers.FactoryReturning(llm),
            AgentTestHelpers.OptionsWith(new AgentsOptions()),
            NullLogger<TicketDecomposerAgent>.Instance);
    }

    [Fact]
    public async Task RunAsync_ParsesTickets_AndDropsNonTaxonomyLabels()
    {
        var json = """
            { "tickets": [
              { "title": "Add SKU uniqueness check", "body": "do it",
                "labels": ["type:feature","area:data","p1","ai:ready","bogus:label"], "aiReady": true }
            ] }
            """;

        var result = await Build(json).RunAsync(DecompositionFixtures.Spec(), DecompositionFixtures.Seed());

        result.Count.ShouldBe(1);
        result[0].Title.ShouldBe("Add SKU uniqueness check");
        result[0].Labels.ShouldNotContain("bogus:label");
        result[0].Labels.ShouldContain(StandardLabels.AiReady);
        result[0].AiReady.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_AiReadyFalse_ForcesNeedsHumanAndDropsAiReady()
    {
        var json = """
            { "tickets": [
              { "title": "Coordinate the epic", "body": "", "labels": ["type:feature","area:core","p1","ai:ready"], "aiReady": false }
            ] }
            """;

        var result = await Build(json).RunAsync(DecompositionFixtures.Spec(), DecompositionFixtures.Seed());

        result[0].AiReady.ShouldBeFalse();
        result[0].Labels.ShouldContain(StandardLabels.NeedsHuman);
        result[0].Labels.ShouldNotContain(StandardLabels.AiReady);
    }

    [Fact]
    public async Task RunAsync_UnparseableResponse_FallsBackToSeed()
    {
        var seed = DecompositionFixtures.Seed();

        var result = await Build("not json at all").RunAsync(DecompositionFixtures.Spec(), seed);

        result.ShouldBeSameAs(seed);
    }

    [Fact]
    public async Task RunAsync_EmptyTicketArray_FallsBackToSeed()
    {
        var seed = DecompositionFixtures.Seed();

        var result = await Build("""{ "tickets": [] }""").RunAsync(DecompositionFixtures.Spec(), seed);

        result.ShouldBeSameAs(seed);
    }
}
